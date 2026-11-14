using DXAllert.Core;
using DXAllert.Data.Sources;

namespace DXAllert.Data;

public static class Http
{
    public static HttpClient Create()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DXAllert/1.0 (programma radioamatoriale desktop)");
        return http;
    }
}

/// <summary>File cty.dat: copia locale aggiornata ogni 7 giorni, altrimenti quella inclusa nel programma.</summary>
public static class CtyProvider
{
    public const string Url = "https://www.country-files.com/cty/cty.dat";

    public static async Task<CtyResolver> LoadAsync(HttpClient http, string bundledPath, CancellationToken ct)
    {
        var local = AppPaths.CtyFile;
        try
        {
            if (!File.Exists(local) || DateTime.UtcNow - File.GetLastWriteTimeUtc(local) > TimeSpan.FromDays(7))
            {
                var text = await http.GetStringAsync(Url, ct);
                if (CtyResolver.Parse(text).Entities.Count > 300)
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                    await File.WriteAllTextAsync(local, text, ct);
                    Log.Info("cty.dat aggiornato da country-files.com");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Aggiornamento cty.dat non riuscito ({ex.Message}), uso la copia locale");
        }
        var path = File.Exists(local) ? local : bundledPath;
        return CtyResolver.Parse(await File.ReadAllTextAsync(path, ct));
    }
}

public sealed record SourceStatus(string Name, bool Ok, bool FromCache, DateTime? FetchedUtc, int Count, string Error);

public sealed record RefreshResult(List<Dxpedition> Items, List<SourceStatus> Sources, List<Dxpedition> NewlyAnnounced);

/// <summary>Interroga tutte le fonti, usa la cache se una non risponde, unisce e risolve le entità.</summary>
public sealed class DxpeditionService
{
    private readonly HttpClient _http;
    private readonly Repository _repo;
    // L'ordine è la priorità dei campi nell'unione.
    private readonly IDxpeditionSource[] _sources;

    public DxpeditionService(HttpClient http, Repository repo, IDxpeditionSource[]? sources = null)
    {
        _http = http;
        _repo = repo;
        _sources = sources ?? new IDxpeditionSource[]
        {
            new DxZoneSource(), new Dx425Source(), new Ng3kSource(), new DxWorldSource(), new DxNewsSource(),
        };
    }

    // Con aggiornamenti frequenti si scrive nel log solo quando lo stato di una fonte cambia.
    private readonly Dictionary<string, string> _lastLogged = new();

    private void LogOnChange(string source, string message, bool warn)
    {
        if (_lastLogged.TryGetValue(source, out var last) && last == message) return;
        _lastLogged[source] = message;
        if (warn) Log.Warn(message); else Log.Info(message);
    }

    public async Task<RefreshResult> RefreshAsync(CtyResolver cty, bool offlineOnly, CancellationToken ct)
    {
        var lists = new List<IReadOnlyList<Dxpedition>>();
        var status = new List<SourceStatus>();

        foreach (var src in _sources)
        {
            if (src is DxNewsSource { FeedUrl: null or "" }) continue;
            IReadOnlyList<Dxpedition>? items = null;
            var error = "";
            if (!offlineOnly)
            {
                try
                {
                    items = await src.FetchAsync(_http, ct);
                    if (items.Count == 0) throw new InvalidDataException("nessuna voce riconosciuta (formato cambiato?)");
                    _repo.SaveSourceCache(src.Name, items, DateTime.UtcNow);
                    status.Add(new SourceStatus(src.Name, true, false, DateTime.UtcNow, items.Count, ""));
                    LogOnChange(src.Name, $"Fonte {src.Name}: {items.Count} voci", warn: false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    items = null;
                    error = ex.Message;
                    LogOnChange(src.Name, $"Fonte {src.Name} non disponibile: {ex.Message}", warn: true);
                }
            }
            if (items is null)
            {
                var cached = _repo.LoadSourceCache(src.Name);
                items = cached?.Items ?? new List<Dxpedition>();
                status.Add(new SourceStatus(src.Name, false, cached is not null, cached?.FetchedUtc, items.Count, error));
            }
            lists.Add(items);
        }

        var merged = DxpeditionMerger.Merge(lists);
        foreach (var d in merged) d.Entity = cty.Resolve(d.Callsign, d.EntityName);

        // "Nuova annunciata": chiave mai vista prima. Al primo avvio si registra tutto senza avvisare.
        var firstRun = !_repo.HasKnownCalls();
        var now = DateTime.UtcNow;
        var fresh = new List<Dxpedition>();
        foreach (var d in merged)
        {
            if (d.IsSpecialEvent) continue;
            // Le voci senza date (solo notizie) contano come nuove solo se il nominativo è mai visto.
            var newCall = _repo.MarkKnown(d.Callsign);
            var year = (d.StartUtc ?? d.EndUtc)?.Year;
            var isNew = year is null ? newCall : _repo.MarkKnown($"{d.Callsign}|{year}");
            if (isNew && !firstRun && d.StatusAt(now) is DxStatus.Planned or DxStatus.Undated)
                fresh.Add(d);
        }
        return new RefreshResult(merged, status, fresh);
    }
}
