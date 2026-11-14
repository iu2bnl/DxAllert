namespace DXAllert.Core;

/// <summary>Entità DXCC ricavata dal file cty.dat.</summary>
public sealed record DxccEntity(
    string Name, int CqZone, int ItuZone, string Continent,
    double Latitude, double Longitude, string PrimaryPrefix);

public enum DxStatus { Planned, Active, Ended, Undated }

/// <summary>Una DXpedition annunciata da una o più fonti.</summary>
public sealed class Dxpedition
{
    public string Callsign { get; set; } = "";
    /// <summary>Nome dell'entità come scritto dalla fonte.</summary>
    public string EntityName { get; set; } = "";
    public DateTime? StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public string Bands { get; set; } = "";
    public string Modes { get; set; } = "";
    public string QslVia { get; set; } = "";
    public string Url { get; set; } = "";
    public string Iota { get; set; } = "";
    public string Info { get; set; } = "";
    public bool IsSpecialEvent { get; set; }
    public List<string> Sources { get; set; } = new();

    /// <summary>Entità DXCC risolta dal nominativo (non serializzata nella cache).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DxccEntity? Entity { get; set; }

    public DxStatus StatusAt(DateTime nowUtc)
    {
        if (StartUtc is null && EndUtc is null) return DxStatus.Undated;
        var today = nowUtc.Date;
        if (EndUtc is { } e && e.Date < today) return DxStatus.Ended;
        if (StartUtc is { } s && s.Date > today) return DxStatus.Planned;
        return DxStatus.Active;
    }
}

/// <summary>Uno spot DX ricevuto dal cluster o dalla fonte web.</summary>
public sealed record Spot(
    DateTime TimeUtc, string DxCall, double FreqKhz, string Spotter, string Comment, string Source)
{
    public string Band => BandPlan.BandOf(FreqKhz);
    public string Mode => BandPlan.ModeOf(FreqKhz, Comment);
}

/// <summary>Impostazioni dell'utente, salvate in settings.json.</summary>
public sealed class AppSettings
{
    public string Callsign { get; set; } = "";
    public string Locator { get; set; } = "";
    /// <summary>Nodi DX cluster "host:porta", collegati tutti in parallelo.</summary>
    public List<string> ClusterNodes { get; set; } = new() { "iq8do.aricaserta.it:7300" };
    public bool UseWebSpotFallback { get; set; } = true;
    public List<string> Bands { get; set; } = new(BandPlan.AllBands);
    public List<string> Modes { get; set; } = new(BandPlan.AllModes);
    /// <summary>Nominativi o entità preferiti (uno per riga).</summary>
    public List<string> Favorites { get; set; } = new();
    public bool AlertOnSpot { get; set; } = true;
    public bool AlertOnlyFavorites { get; set; }
    public bool AlertSound { get; set; } = true;
    public bool IncludeSpecialEvents { get; set; }
    public bool DarkTheme { get; set; }
    public string QrzUser { get; set; } = "";
    /// <summary>Password di QRZ cifrata con la protezione dati di Windows (DPAPI), in base64.</summary>
    public string QrzPasswordProtected { get; set; } = "";
}

/// <summary>Un QSO del log personale, importato da file ADIF.</summary>
public sealed record WorkedEntry(string Callsign, string Band, string Mode, string DxccName);

public interface IDxpeditionSource
{
    /// <summary>Nome mostrato nella colonna "Fonti".</summary>
    string Name { get; }
    Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct);
}

public interface ISpotSource
{
    string Name { get; }
    event Action<Spot>? SpotReceived;
    /// <summary>Testo di stato per la barra in alto (connesso, riconnessione…).</summary>
    event Action<string>? StatusChanged;
    bool IsConnected { get; }
    Task RunAsync(CancellationToken ct);
}
