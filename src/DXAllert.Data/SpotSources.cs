using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DXAllert.Core;

namespace DXAllert.Data;

/// <summary>Connessione telnet a un nodo DX cluster, con riconnessione automatica.</summary>
public sealed class TelnetClusterClient : ISpotSource
{
    private readonly string _host;
    private readonly int _port;
    private readonly Func<string> _callsign;

    public TelnetClusterClient(string host, int port, Func<string> callsign)
    {
        _host = host;
        _port = port;
        _callsign = callsign;
    }

    /// <summary>"host:porta" oppure "host porta"; senza porta vale 7300 (DXSpider).</summary>
    public static bool TryParseNode(string text, out string host, out int port)
    {
        var parts = text.Trim().Split(new[] { ':', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        host = parts.Length > 0 ? parts[0] : "";
        port = 7300;
        if (parts.Length > 2 || host.Length == 0 || Uri.CheckHostName(host) == UriHostNameType.Unknown) return false;
        return parts.Length == 1 || (int.TryParse(parts[1], out port) && port is >= 1 and <= 65535);
    }

    public string Name => _host;
    public string Node => $"{_host}:{_port}";
    public event Action<Spot>? SpotReceived;
    public event Action<string>? StatusChanged;
    public bool IsConnected { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            var call = _callsign().Trim().ToUpperInvariant();
            if (call.Length == 0)
            {
                StatusChanged?.Invoke("inserisci il nominativo nelle impostazioni");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
            var node = Node;
            try
            {
                StatusChanged?.Invoke($"connessione a {node}…");
                using var tcp = new TcpClient();
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(20));
                    await tcp.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false);
                }
                tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                await SessionAsync(tcp.GetStream(), call, node, () => delay = TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                Log.Warn($"Cluster {node}: connessione chiusa dal nodo");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Log.Warn($"Cluster {node}: {ex.Message}"); }
            finally { IsConnected = false; }

            StatusChanged?.Invoke($"{node} non risponde, nuovo tentativo tra {delay.TotalSeconds:0} s");
            try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 300));
        }
    }

    private async Task SessionAsync(NetworkStream stream, string call, string node, Action onLoggedIn, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var pending = new StringBuilder();
        var loggedIn = false;


        while (!ct.IsCancellationRequested)
        {
            int n;
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // Nessun dato per 10 minuti: connessione considerata morta.
                cts.CancelAfter(TimeSpan.FromMinutes(10));
                try { n = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("nessun dato dal nodo per 10 minuti");
                }
            }
            if (n == 0) return;

            // Nominativo cambiato nelle impostazioni: la sessione va riaperta.
            if (!string.Equals(call, _callsign().Trim(), StringComparison.OrdinalIgnoreCase)) return;

            pending.Append(StripTelnet(buffer, n));
            int nl;
            while ((nl = IndexOfNewline(pending)) >= 0)
            {
                var line = pending.ToString(0, nl).Trim('\r', '\a', ' ');
                pending.Remove(0, nl + 1);
                if (line.Length == 0) continue;
                var spot = SpotLineParser.Parse(line, DateTime.UtcNow, _host);
                if (spot is not null)
                {
                    if (!loggedIn) { loggedIn = true; MarkConnected(node, onLoggedIn); }
                    SpotReceived?.Invoke(spot);
                }
            }

            // Il prompt di login arriva senza a-capo.
            var tail = pending.ToString().ToLowerInvariant();
            if (!loggedIn && (tail.Contains("login:") || tail.Contains("call:") || tail.Contains("callsign:")))
            {
                pending.Clear();
                await SendAsync(stream, call, ct).ConfigureAwait(false);
                loggedIn = true;
                MarkConnected(node, onLoggedIn);
                Log.Info($"Cluster {node}: login come {call}");
            }
            if (pending.Length > 8192) pending.Clear();
        }
    }

    private void MarkConnected(string node, Action onLoggedIn)
    {
        IsConnected = true;
        onLoggedIn();
        StatusChanged?.Invoke($"connesso a {node}");
    }

    private static Task SendAsync(NetworkStream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text + "\r\n"), ct).AsTask();

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') return i;
        return -1;
    }

    /// <summary>Toglie le sequenze di negoziazione telnet (IAC = 0xFF + 2 byte).</summary>
    private static string StripTelnet(byte[] data, int count)
    {
        var sb = new StringBuilder(count);
        for (int i = 0; i < count; i++)
        {
            if (data[i] == 0xFF) { i += 2; continue; }
            if (data[i] == 0) continue;
            sb.Append((char)(data[i] < 0x80 ? data[i] : (byte)'?'));
        }
        return sb.ToString();
    }
}

/// <summary>Riserva web: API JSON di DXSummit, interrogata solo quando il cluster telnet è scollegato.</summary>
public sealed class DxSummitSpotSource : ISpotSource
{
    public const string Url = "http://www.dxsummit.fi/api/v1/spots?limit=50";
    private readonly HttpClient _http;
    private readonly Func<bool> _shouldPoll;
    private long _lastId;

    public DxSummitSpotSource(HttpClient http, Func<bool> shouldPoll)
    {
        _http = http;
        _shouldPoll = shouldPoll;
    }

    public string Name => "DXSummit";
    public event Action<Spot>? SpotReceived;
    public event Action<string>? StatusChanged;
    public bool IsConnected { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        // Lascia al telnet il tempo di collegarsi prima di partire con la riserva.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            if (_shouldPoll())
            {
                try
                {
                    var json = await _http.GetStringAsync(Url, ct).ConfigureAwait(false);
                    var spots = Parse(json, Name, ref _lastId);
                    foreach (var s in spots) SpotReceived?.Invoke(s);
                    if (!IsConnected) Log.Info("Spot dalla riserva web DXSummit");
                    IsConnected = true;
                    StatusChanged?.Invoke("Spot: riserva web DXSummit");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    IsConnected = false;
                    Log.Warn($"DXSummit: {ex.Message}");
                }
            }
            else IsConnected = false;
            try { await Task.Delay(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Restituisce gli spot con id maggiore di lastId, dal più vecchio al più recente.</summary>
    public static List<Spot> Parse(string json, string source, ref long lastId)
    {
        var list = new List<(long Id, Spot Spot)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var id = e.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var v) ? v : 0;
            if (id <= lastId) continue;
            if (!e.TryGetProperty("dx_call", out var call) || !e.TryGetProperty("frequency", out var freq)) continue;
            var time = e.TryGetProperty("time", out var t) && DateTime.TryParse(t.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : DateTime.UtcNow;
            list.Add((id, new Spot(time, (call.GetString() ?? "").ToUpperInvariant(), freq.GetDouble(),
                e.TryGetProperty("de_call", out var de) ? de.GetString() ?? "" : "",
                e.TryGetProperty("info", out var info) ? info.GetString() ?? "" : "", source)));
        }
        list.Sort((a, b) => a.Id.CompareTo(b.Id));
        if (list.Count > 0) lastId = list[^1].Id;
        return list.Select(x => x.Spot).ToList();
    }
}
