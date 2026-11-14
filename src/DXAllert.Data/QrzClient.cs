using System.Globalization;
using System.Xml.Linq;
using DXAllert.Core;

namespace DXAllert.Data;

/// <summary>Scheda di un nominativo dal servizio XML di QRZ.com.</summary>
public sealed record QrzInfo
{
    public string Call { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public string Country { get; init; } = "";
    public string Grid { get; init; } = "";
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string CqZone { get; init; } = "";
    public string ItuZone { get; init; } = "";
    public string Iota { get; init; } = "";
    public string QslManager { get; init; } = "";
    public string Email { get; init; } = "";
    public string Web { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string Lotw { get; init; } = "";
    public string Eqsl { get; init; } = "";
    public string PaperQsl { get; init; } = "";
    public string Aliases { get; init; } = "";
}

public sealed class QrzException : Exception
{
    public QrzException(string message) : base(message) { }
}

/// <summary>
/// Client del servizio XML di QRZ.com (https://xmldata.qrz.com/xml/current/).
/// Serve un account QRZ; senza abbonamento XML il servizio restituisce solo pochi campi.
/// </summary>
public sealed class QrzClient
{
    public const string Endpoint = "https://xmldata.qrz.com/xml/current/";
    private static readonly XNamespace Ns = "http://xmldata.qrz.com";

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Una scheda cambia di rado: si interroga QRZ una sola volta per nominativo e sessione di lavoro.
    private readonly Dictionary<string, QrzInfo?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string _user = "";
    private string _password = "";
    private string? _sessionKey;

    public QrzClient(HttpClient http) => _http = http;

    public bool HasCredentials => _user.Length > 0 && _password.Length > 0;
    /// <summary>Scadenza dell'abbonamento XML come la riporta QRZ ("non-subscriber" se assente).</summary>
    public string Subscription { get; private set; } = "";

    public void SetCredentials(string user, string password)
    {
        if (user == _user && password == _password) return;
        _user = user;
        _password = password;
        _sessionKey = null;
        Subscription = "";
        lock (_cache) _cache.Clear();
    }

    /// <summary>Null se il nominativo non è nel database di QRZ.</summary>
    public async Task<QrzInfo?> LookupAsync(string callsign, CancellationToken ct)
    {
        var call = callsign.Trim().ToUpperInvariant();
        lock (_cache)
            if (_cache.TryGetValue(call, out var cached)) return cached;
        if (!HasCredentials) throw new QrzException("Credenziali QRZ non impostate");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                _sessionKey ??= await LoginAsync(ct).ConfigureAwait(false);
                var xml = await PostAsync(new() { ["s"] = _sessionKey, ["callsign"] = call }, ct).ConfigureAwait(false);
                var (info, error) = ParseLookup(xml);
                if (error.Length == 0 || error.StartsWith("Not found", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_cache) _cache[call] = info;
                    return info;
                }
                // Sessione scaduta: un solo nuovo login, poi si rinuncia.
                var expired = error.Contains("Session", StringComparison.OrdinalIgnoreCase);
                if (!expired || attempt > 0) throw new QrzException(error);
                _sessionKey = null;
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<string> LoginAsync(CancellationToken ct)
    {
        var xml = await PostAsync(new() { ["username"] = _user, ["password"] = _password, ["agent"] = "DXAllert1.0" }, ct)
            .ConfigureAwait(false);
        var (key, subscription, error) = ParseSession(xml);
        if (key.Length == 0) throw new QrzException(error.Length > 0 ? error : "Accesso a QRZ non riuscito");
        Subscription = subscription;
        Log.Info($"QRZ: accesso riuscito (abbonamento XML: {(subscription.Length > 0 ? subscription : "n/d")})");
        return key;
    }

    // Sempre in POST: utente e password non finiscono nell'indirizzo della richiesta.
    private async Task<string> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(Endpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public static (string Key, string Subscription, string Error) ParseSession(string xml)
    {
        var session = XDocument.Parse(xml).Root?.Element(Ns + "Session");
        return (Text(session, "Key"), Text(session, "SubExp"), Text(session, "Error"));
    }

    public static (QrzInfo? Info, string Error) ParseLookup(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        var error = Text(root?.Element(Ns + "Session"), "Error");
        var c = root?.Element(Ns + "Callsign");
        if (c is null) return (null, error.Length > 0 ? error : "Risposta di QRZ senza dati");

        var address = string.Join(", ", new[]
        {
            Text(c, "addr1"),
            string.Join(" ", new[] { Text(c, "zip"), Text(c, "addr2"), Text(c, "state") }.Where(s => s.Length > 0)),
        }.Where(s => s.Length > 0));
        var info = new QrzInfo
        {
            Call = Text(c, "call"),
            Name = Text(c, "name_fmt") is { Length: > 0 } formatted
                ? formatted
                : string.Join(" ", new[] { Text(c, "fname"), Text(c, "name") }.Where(s => s.Length > 0)),
            Address = address, Country = Text(c, "country"), Grid = Text(c, "grid"),
            Latitude = Number(c, "lat"), Longitude = Number(c, "lon"),
            CqZone = Text(c, "cqzone"), ItuZone = Text(c, "ituzone"), Iota = Text(c, "iota"),
            QslManager = Text(c, "qslmgr"), Email = Text(c, "email"), Web = Text(c, "url"),
            ImageUrl = Text(c, "image"), Lotw = Text(c, "lotw"), Eqsl = Text(c, "eqsl"), PaperQsl = Text(c, "mqsl"),
            Aliases = Text(c, "aliases"),
        };
        return (info, "");
    }

    private static string Text(XElement? parent, string name) => parent?.Element(Ns + name)?.Value.Trim() ?? "";

    private static double? Number(XElement parent, string name) =>
        double.TryParse(Text(parent, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
