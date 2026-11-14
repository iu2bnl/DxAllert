using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public static readonly TimeSpan OnAirWindow = TimeSpan.FromMinutes(15);

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
    /// <summary>Dal log ADIF: "✓ 20m FT8" (nominativo già collegato), "~ 20m SSB" (solo l'entità), vuoto se nuovo.</summary>
    public string WorkedText { get; }

    public int? DaysLeft => Dx.EndUtc is { } e ? (int)(e.Date - DateTime.UtcNow.Date).TotalDays : null;
    public string DaysLeftText => DaysLeft switch { null => "", 0 => "ultimo giorno", 1 => "1 giorno", var n => $"{n} giorni" };
    public int? DaysToStart => Dx.StartUtc is { } s ? (int)(s.Date - DateTime.UtcNow.Date).TotalDays : null;
    public string DaysToStartText => DaysToStart switch { null => "", <= 0 => "", 1 => "domani", var n => $"tra {n} giorni" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSpotText))]
    private Spot? _lastSpot;

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

/// <summary>Riga della scheda "Tempo reale".</summary>
public sealed class SpotRow
{
    public SpotRow(Spot s, DxRow? dx, DxccEntity? entity = null, WorkedLog? worked = null)
    {
        Spot = s;
        IsDxpedition = dx is not null;
        entity = dx?.Dx.Entity ?? entity;
        Entity = dx?.Entity ?? entity?.Name ?? "";
        EntityLat = entity?.Latitude;
        EntityLon = entity?.Longitude;
        WorkedText = worked?.SummaryFor(s.DxCall, Entity) ?? "";
    }

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

public sealed record MonthCard(string Title, bool IsCurrent, List<string> Lines)
{
    public string Count => Lines.Count == 0 ? "nessuna" : Lines.Count.ToString();
}
