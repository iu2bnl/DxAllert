using System.Globalization;
using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Parser delle righe "DX de" dei nodi DX cluster (DXSpider, AR-Cluster, CC Cluster).</summary>
public static class SpotLineParser
{
    // DX de IK2CLB:     7025.2  VK2ARZ       loud                           1919Z JN45
    private static readonly Regex Line = new(
        @"^DX de\s+([A-Z0-9/\-#]+?):?\s+(\d+(?:\.\d+)?)\s+([A-Z0-9/]+)\s*(.*?)\s*(\d{4})Z(?:\s+\S+)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Spot? Parse(string line, DateTime nowUtc, string source)
    {
        var m = Line.Match(line.Trim('\a', '\r', '\n', ' '));
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
            return null;
        int hh = int.Parse(m.Groups[5].Value[..2]), mm = int.Parse(m.Groups[5].Value[2..]);
        if (hh > 23 || mm > 59) return null;
        var time = nowUtc.Date.AddHours(hh).AddMinutes(mm);
        // Lo spot riporta solo HHMM: a cavallo della mezzanotte appartiene al giorno prima.
        if (time > nowUtc.AddMinutes(10)) time = time.AddDays(-1);
        var spotter = Regex.Replace(m.Groups[1].Value.ToUpperInvariant(), @"-\d+$|-#$", "");
        return new Spot(time, m.Groups[3].Value.ToUpperInvariant(), freq, spotter, m.Groups[4].Value.Trim(), source);
    }
}

public enum AlertKind { OnAir }

public sealed record Alert(AlertKind Kind, string Title, string Text, Dxpedition Dxpedition);

/// <summary>Un solo avviso quando una DXpedition compare on air: finché continua a essere spottata
/// (su una banda e un modo di interesse) non si ripete; torna dopo una pausa più lunga di <see cref="OnAirWindow"/>.</summary>
public sealed class AlertEngine
{
    public static readonly TimeSpan OnAirWindow = TimeSpan.FromMinutes(15);
    private readonly Dictionary<string, DateTime> _lastSeen = new(StringComparer.Ordinal);

    public Alert? OnSpot(Spot spot, Dxpedition dx, AppSettings s, DateTime nowUtc)
    {
        if (!s.AlertOnSpot) return null;
        var band = spot.Band;
        var mode = spot.Mode;
        if (band.Length == 0 || !s.Bands.Contains(band)) return null;
        if (mode.Length > 0 && !s.Modes.Contains(mode)) return null;
        if (s.AlertOnlyFavorites && !IsFavorite(dx, s)) return null;

        var key = CallsignUtil.Normalize(spot.DxCall);
        var alreadyOnAir = _lastSeen.TryGetValue(key, out var last) && nowUtc - last <= OnAirWindow;
        _lastSeen[key] = nowUtc;
        if (alreadyOnAir) return null;

        var entity = dx.Entity?.Name ?? dx.EntityName;
        return new Alert(AlertKind.OnAir, $"{spot.DxCall} ON AIR – {band} {mode}".TrimEnd(),
            $"{entity} · {spot.FreqKhz.ToString("0.0", CultureInfo.InvariantCulture)} kHz · spot di {spot.Spotter}" +
            (spot.Comment.Length > 0 ? $"\n{spot.Comment}" : ""), dx);
    }


    public static bool IsFavorite(Dxpedition dx, AppSettings s)
    {
        foreach (var raw in s.Favorites)
        {
            var f = raw.Trim();
            if (f.Length == 0) continue;
            if (string.Equals(f, dx.Callsign, StringComparison.OrdinalIgnoreCase)) return true;
            if (dx.Entity is { } e && (string.Equals(f, e.Name, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(f, e.PrimaryPrefix, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (dx.EntityName.Length > 0 && string.Equals(f, dx.EntityName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>Log su file giornaliero in %AppData%\DXAllert\logs.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DXAllert", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"dxallert-{DateTime.UtcNow:yyyyMMdd}.log");

    public static void Info(string msg) => Write("INFO ", msg);
    public static void Warn(string msg) => Write("AVVISO", msg);
    public static void Error(string msg, Exception? ex = null) =>
        Write("ERRORE", ex is null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z {level} {msg}{Environment.NewLine}");
            }
        }
        catch { /* il log non deve mai far cadere il programma */ }
    }
}
