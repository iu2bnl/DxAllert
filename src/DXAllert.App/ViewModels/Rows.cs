using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DXAllert.App.Services;
using DXAllert.Core;

namespace DXAllert.App.ViewModels;

/// <summary>Formato degli orari: sempre UTC, con l'ora locale tra parentesi.</summary>
public static class Fmt
{
    public static string Time(DateTime utc) =>
        $"{utc:HH:mm} UTC ({DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime():HH:mm})";

    public static string DateTimeUtc(DateTime utc) =>
        $"{utc:dd/MM HH:mm} UTC ({DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime():HH:mm})";

    public static string Date(DateTime? d) => d?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
}

/// <summary>Riga delle tabelle "Attive ora" e "Pianificate".</summary>
public partial class DxRow : ObservableObject
{
    public static readonly TimeSpan OnAirWindow = AlertEngine.OnAirWindow;

    public DxRow(Dxpedition dx, (double Lat, double Lon)? home, bool favorite, WorkedLog? worked = null)
    {
        Dx = dx;
        IsFavorite = favorite;
        if (home is { } h && dx.Entity is { } e)
        {
            DistanceKm = Math.Round(Geo.DistanceKm(h.Lat, h.Lon, e.Latitude, e.Longitude));
            Azimuth = Math.Round(Geo.BearingDeg(h.Lat, h.Lon, e.Latitude, e.Longitude));
        }
        WorkedText = worked?.SummaryFor(dx.Callsign, dx.Entity?.Name ?? dx.EntityName) ?? "";
    }

    public Dxpedition Dx { get; }
    public string Callsign => Dx.Callsign;
    public string Entity => Dx.Entity?.Name ?? Dx.EntityName;
    public string Continent => Dx.Entity?.Continent ?? "";
    public string CqZone => Dx.Entity is { } e ? e.CqZone.ToString() : "";
    public string ItuZone => Dx.Entity is { } e ? e.ItuZone.ToString() : "";
    public DateTime? Start => Dx.StartUtc;
    public DateTime? End => Dx.EndUtc;
    public string StartText => Fmt.Date(Dx.StartUtc);
    public string EndText => Fmt.Date(Dx.EndUtc);
    public string Bands => Dx.Bands;
    public string Modes => Dx.Modes;
    public string Qsl => Dx.QslVia;
    public string Iota => Dx.Iota;
    public string Url => Dx.Url.Length > 0 ? Dx.Url : $"https://www.qrz.com/db/{Dx.Callsign}";
    public string Sources => string.Join(", ", Dx.Sources);
    public string Info => Dx.Info;
    public bool IsFavorite { get; }
    public string FavoriteMark => IsFavorite ? "★" : "";
    public double? DistanceKm { get; }
    public double? Azimuth { get; }
    public string DistanceText => DistanceKm is { } d ? $"{d:0} km" : "";
    public string AzimuthText => Azimuth is { } a ? $"{a:0}°" : "";
    public string ContinentColor => ContinentColors.Of(Continent);

    // Stessi nomi delle proprietà di SpotRow: le celle in stile cluster sono condivise tra le schede.
    public string DxCall => Callsign;
    public string? DxFlag => DxccInfo.Flag(Dx.Entity);
    public string DxCode => DxccInfo.Code(Dx.Entity);
    public string DxCountry => Entity.ToUpperInvariant();

    /// <summary>Riconoscimento DXCC dei nominativi, impostato quando cty.dat è caricato.</summary>
    public static Func<string, DxccEntity?>? ResolveCall { get; set; }
    public string Spotter => LastSpot?.Spotter ?? "";
    public string? SpotterFlag => LastSpot is { } s ? DxccInfo.Flag(ResolveCall?.Invoke(s.Spotter)) : null;
    public string SpotterCountry => LastSpot is { } s ? ResolveCall?.Invoke(s.Spotter)?.Name.ToUpperInvariant() ?? "" : "";
    /// <summary>Dal log ADIF: "✓ 20m FT8" (nominativo già collegato), "~ 20m SSB" (solo l'entità), vuoto se nuovo.</summary>
    public string WorkedText { get; }

    public int? DaysLeft => Dx.EndUtc is { } e ? (int)(e.Date - DateTime.UtcNow.Date).TotalDays : null;
    public string DaysLeftText => DaysLeft switch { null => "", 0 => "ultimo giorno", 1 => "1 giorno", var n => $"{n} giorni" };
    public int? DaysToStart => Dx.StartUtc is { } s ? (int)(s.Date - DateTime.UtcNow.Date).TotalDays : null;
    public string DaysToStartText => DaysToStart switch { null => "", <= 0 => "", 1 => "domani", var n => $"tra {n} giorni" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSpotText))]
    [NotifyPropertyChangedFor(nameof(LastSpotTime))]
    [NotifyPropertyChangedFor(nameof(Spotter))]
    [NotifyPropertyChangedFor(nameof(SpotterFlag))]
    [NotifyPropertyChangedFor(nameof(SpotterCountry))]
    private Spot? _lastSpot;

    public string LastSpotTime => LastSpot is { } s ? Fmt.Time(s.TimeUtc) : "";

    [ObservableProperty] private bool _isOnAir;

    public string LastSpotText => LastSpot is { } s
        ? $"{s.FreqKhz.ToString("0.0", CultureInfo.InvariantCulture)} {s.Mode} · " +
          (s.TimeUtc.Date == DateTime.UtcNow.Date ? Fmt.Time(s.TimeUtc) : Fmt.DateTimeUtc(s.TimeUtc))
        : "";

    public List<Spot> RecentSpots { get; } = new();

    public void AddSpot(Spot s)
    {
        RecentSpots.Insert(0, s);
        if (RecentSpots.Count > 8) RecentSpots.RemoveAt(RecentSpots.Count - 1);
        if (LastSpot is null || s.TimeUtc >= LastSpot.TimeUtc) LastSpot = s;
        UpdateOnAir(DateTime.UtcNow);
    }

    public void UpdateOnAir(DateTime nowUtc) =>
        IsOnAir = LastSpot is { } s && nowUtc - s.TimeUtc <= OnAirWindow;

    public void RefreshDays()
    {
        OnPropertyChanged(nameof(DaysLeftText));
        OnPropertyChanged(nameof(DaysToStartText));
    }
}

/// <summary>Riga della scheda "On Air": uno spot, con i dati della DXpedition quando è una di quelle attive.</summary>
public sealed class SpotRow
{
    public SpotRow(Spot s, DxRow? dx, DxccEntity? entity = null, WorkedLog? worked = null, DxccEntity? spotterEntity = null,
                   (double Lat, double Lon)? home = null)
    {
        Spot = s;
        Dx = dx;
        IsDxpedition = dx is not null;
        entity = dx?.Dx.Entity ?? entity;
        Entity = dx?.Entity ?? entity?.Name ?? "";
        EntityLat = entity?.Latitude;
        EntityLon = entity?.Longitude;
        WorkedText = worked?.SummaryFor(s.DxCall, Entity) ?? "";
        DxFlag = DxccInfo.Flag(entity);
        DxCode = DxccInfo.Code(entity);
        SpotterFlag = DxccInfo.Flag(spotterEntity);
        SpotterCode = DxccInfo.Code(spotterEntity);
        SpotterCountry = spotterEntity?.Name.ToUpperInvariant() ?? "";
        if (home is { } h && entity is { } e)
        {
            DistanceKm = Math.Round(Geo.DistanceKm(h.Lat, h.Lon, e.Latitude, e.Longitude));
            Azimuth = Math.Round(Geo.BearingDeg(h.Lat, h.Lon, e.Latitude, e.Longitude));
        }
    }

    /// <summary>La DXpedition attiva a cui appartiene lo spot, o null per uno spot qualsiasi.</summary>
    public DxRow? Dx { get; }
    public string FavoriteMark => Dx?.FavoriteMark ?? "";
    public string DaysLeftText => Dx?.DaysLeftText ?? "";
    public int? DaysLeft => Dx?.DaysLeft;
    public string Qsl => Dx?.Qsl ?? "";
    public double? DistanceKm { get; }
    public double? Azimuth { get; }
    public string DistanceText => DistanceKm is { } d ? $"{d:0} km" : "";
    public string AzimuthText => Azimuth is { } a ? $"{a:0}°" : "";

    public string? DxFlag { get; }
    public string DxCode { get; }
    public string DxCountry => Entity.ToUpperInvariant();
    public string? SpotterFlag { get; }
    public string SpotterCode { get; }
    public string SpotterCountry { get; }
    public string TimeShort => Spot.TimeUtc.ToString("HH:mm", CultureInfo.InvariantCulture);

    public string WorkedText { get; }

    public Spot Spot { get; }
    public bool IsDxpedition { get; }
    public string Entity { get; }
    public double? EntityLat { get; }
    public double? EntityLon { get; }
    public string Time => Fmt.Time(Spot.TimeUtc);
    public string DxCall => Spot.DxCall;
    public string Freq => Spot.FreqKhz.ToString("0.0", CultureInfo.InvariantCulture);
    public string Band => Spot.Band;
    public string Mode => Spot.Mode;
    public string Spotter => Spot.Spotter;
    public string Comment => Spot.Comment;
    public string Source => Spot.Source;
}

public partial class CheckItem : ObservableObject
{
    public CheckItem(string name, bool isChecked)
    {
        Name = name;
        _isChecked = isChecked;
    }

    public string Name { get; }
    [ObservableProperty] private bool _isChecked;
}

/// <summary>Colore di ciascun continente nel calendario e nella sua legenda.</summary>
public static class ContinentColors
{
    public static string Of(string continent) => continent switch
    {
        "EU" => "#42A5F5",
        "AF" => "#FFA726",
        "AS" => "#EF5350",
        "NA" => "#66BB6A",
        "SA" => "#AB47BC",
        "OC" => "#26C6DA",
        "AN" => "#B0BEC5",
        _ => "#8D99A6",
    };

    public static readonly IReadOnlyList<LegendItem> Legend = new[]
    {
        new LegendItem("Europa", Of("EU")), new LegendItem("Africa", Of("AF")), new LegendItem("Asia", Of("AS")),
        new LegendItem("Nord America", Of("NA")), new LegendItem("Sud America", Of("SA")),
        new LegendItem("Oceania", Of("OC")), new LegendItem("Antartide", Of("AN")),
    };
}

public sealed record LegendItem(string Name, string Color);

/// <summary>Etichetta di una DXpedition che inizia in un giorno del calendario.</summary>
public sealed class CalendarChip
{
    public CalendarChip(DxRow row) => Row = row;
    public DxRow Row { get; }
    public string Call => Row.Callsign;
    public string Color => Row.ContinentColor;
    public string Tip => $"{Row.Callsign} · {Row.Entity}\n{Row.StartText} – {Row.EndText}";
}

/// <summary>Una casella del calendario mensile.</summary>
public sealed class CalendarDay
{
    private const int MaxChips = 2;

    public CalendarDay(DateTime date, bool isCurrentMonth, bool isToday, List<DxRow> rows, double activity)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        Rows = rows;
        Activity = activity;
        var starts = rows.Where(r => r.Start?.Date == date).ToList();
        Starts = starts.Take(MaxChips).Select(r => new CalendarChip(r)).ToList();
        MoreText = starts.Count > MaxChips ? $"+{starts.Count - MaxChips} iniziano" : "";
    }

    public DateTime Date { get; }
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);
    /// <summary>DXpedition attive in questo giorno.</summary>
    public List<DxRow> Rows { get; }
    /// <summary>Da 0 a 1: quante sono attive rispetto al giorno più affollato del mese.</summary>
    public double Activity { get; }
    public List<CalendarChip> Starts { get; }
    public string MoreText { get; }
    public string ActiveText => Rows.Count switch { 0 => "", 1 => "1 attiva", var n => $"{n} attive" };
}

/// <summary>Riga dell'elenco del giorno selezionato nel calendario.</summary>
public sealed class CalendarEntry
{
    public CalendarEntry(DxRow row, DateTime day)
    {
        Row = row;
        var starts = row.Start?.Date == day;
        var ends = (row.End ?? row.Start)?.Date == day;
        Tag = starts && ends ? "solo oggi" : starts ? "inizia" : ends ? "ultimo giorno" : "";
    }

    public DxRow Row { get; }
    public string Tag { get; }
    public bool HasTag => Tag.Length > 0;
    public bool HasWorked => Row.WorkedText.Length > 0;
    public string Dates => $"{Row.StartText} – {Row.EndText}";
    public string Details => string.Join(" · ", new[] { Dates, Row.Bands, Row.Modes }.Where(s => s.Length > 0));
}

// ---------------------------------------------------------------- contest

public static class ContestColors
{
    public static string Of(ContestRegion r) => r switch
    {
        ContestRegion.Italia => "#2E9E5B",
        ContestRegion.Europa => "#42A5F5",
        ContestRegion.Mondo => "#FFA726",
        _ => "#8D99A6",
    };

    public static string Label(ContestRegion r) => r == ContestRegion.Altro ? "Altri" : r.ToString();

    public static readonly IReadOnlyList<LegendItem> Legend = new[]
    {
        new LegendItem("Italia", Of(ContestRegion.Italia)), new LegendItem("Europa", Of(ContestRegion.Europa)),
        new LegendItem("Mondo", Of(ContestRegion.Mondo)), new LegendItem("Altri (WA7BNM)", Of(ContestRegion.Altro)),
    };
}

/// <summary>Un'edizione di un contest nella tabella e nel calendario.</summary>
public sealed class ContestRow
{
    private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

    public ContestRow(ContestEvent e) => Event = e;

    public ContestEvent Event { get; }
    public string Name => Event.Name;
    public DateTime Start => Event.StartUtc;
    public DateTime End => Event.EndUtc;
    public string Region => ContestColors.Label(Event.Region);
    public string RegionColor => ContestColors.Of(Event.Region);
    public string Modes => Event.Modes;
    public string Bands => Event.Bands;
    public string Url => Event.Url;
    public bool HasUrl => Event.Url.Length > 0;
    public string Source => Event.FromWeeklyFeed ? "WA7BNM (orari confermati)" : "Regola annuale";
    public string StartText => Event.StartUtc.ToString("ddd dd/MM HH:mm", It) + " UTC";
    public string EndText => Event.EndUtc.ToString("ddd dd/MM HH:mm", It) + " UTC";
    public string Period => $"{StartText} → {EndText}";
    public string Details => string.Join(" · ", new[] { Modes, Bands }.Where(s => s.Length > 0));

    public string DurationText
    {
        get
        {
            var h = (Event.EndUtc - Event.StartUtc).TotalHours;
            return h >= 47.5 ? $"{Math.Round(h / 24.0, 1):0.#} giorni" : $"{Math.Round(h):0} ore";
        }
    }

    public string WhenText
    {
        get
        {
            var now = DateTime.UtcNow;
            if (Event.EndUtc <= now) return "concluso";
            if (Event.StartUtc <= now) return "in corso";
            var days = (Event.StartUtc.Date - now.Date).Days;
            return days switch { 0 => "oggi", 1 => "domani", var n => $"tra {n} giorni" };
        }
    }

    public bool IsRunning => Event.StartUtc <= DateTime.UtcNow && DateTime.UtcNow < Event.EndUtc;
}

/// <summary>Una casella del calendario dei contest.</summary>
public sealed class ContestDay
{
    private const int MaxChips = 2;

    public ContestDay(DateTime date, bool isCurrentMonth, bool isToday, List<ContestRow> rows)
    {
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        Rows = rows;
        Chips = rows.Take(MaxChips).ToList();
        MoreText = rows.Count > MaxChips ? $"+{rows.Count - MaxChips} altri" : "";
    }

    public DateTime Date { get; }
    public bool IsCurrentMonth { get; }
    public bool IsToday { get; }
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);
    public List<ContestRow> Rows { get; }
    public List<ContestRow> Chips { get; }
    public string MoreText { get; }
    public string CountText => Rows.Count switch { 0 => "", 1 => "1 contest", var n => $"{n} contest" };
}