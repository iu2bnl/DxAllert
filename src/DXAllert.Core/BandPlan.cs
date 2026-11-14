using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Frequenza -> banda e stima del modo (IARU Regione 1, semplificato).</summary>
public static class BandPlan
{
    public static readonly string[] AllBands =
        { "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m" };

    public static readonly string[] AllModes = { "CW", "SSB", "FT8", "FT4", "RTTY" };

    // (banda, inizio kHz, fine kHz, fine del segmento CW kHz)
    private static readonly (string Band, double Lo, double Hi, double CwEnd)[] Segments =
    {
        ("160m", 1800, 2000, 1838), ("80m", 3500, 4000, 3570), ("60m", 5250, 5450, 5354),
        ("40m", 7000, 7300, 7040), ("30m", 10100, 10150, 10130), ("20m", 14000, 14350, 14070),
        ("17m", 18068, 18168, 18095), ("15m", 21000, 21450, 21070), ("12m", 24890, 24990, 24915),
        ("10m", 28000, 29700, 28070), ("6m", 50000, 54000, 50100),
    };

    private static readonly double[] Ft8 =
        { 1840, 3573, 5357, 7074, 10136, 14074, 18100, 21074, 24915, 28074, 50313, 50323 };
    private static readonly double[] Ft4 =
        { 3575, 7047.5, 10140, 14080, 18104, 21140, 24919, 28180, 50318 };

    private static readonly Regex ModeInComment = new(
        @"\b(FT8|FT4|CW|SSB|USB|LSB|RTTY|PSK\d*|FM|AM)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string BandOf(double freqKhz)
    {
        foreach (var s in Segments)
            if (freqKhz >= s.Lo && freqKhz <= s.Hi) return s.Band;
        return "";
    }

    public static string ModeOf(double freqKhz, string? comment)
    {
        if (!string.IsNullOrEmpty(comment))
        {
            var m = ModeInComment.Match(comment);
            if (m.Success)
            {
                var v = m.Groups[1].Value.ToUpperInvariant();
                return v is "USB" or "LSB" ? "SSB" : v;
            }
        }
        // Le dial frequency FT8/FT4 coprono circa 3 kHz di audio.
        foreach (var f in Ft8) if (freqKhz >= f - 0.5 && freqKhz <= f + 3.2) return "FT8";
        foreach (var f in Ft4) if (freqKhz >= f - 0.5 && freqKhz <= f + 3.2) return "FT4";
        foreach (var s in Segments)
            if (freqKhz >= s.Lo && freqKhz <= s.Hi) return freqKhz < s.CwEnd ? "CW" : "SSB";
        return "";
    }
}

/// <summary>Estrae bande e modi dal testo libero di un annuncio ("80-10m; CW SSB FT8").</summary>
public static class InfoExtractor
{
    private static readonly Regex BandRange = new(
        @"\b(160|80|60|40|30|20|17|15|12|10|6)\s*-\s*(160|80|60|40|30|20|17|15|12|10|6)\s*(?:m\b|metres|meters)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BandList = new(
        @"\b((?:(?:160|80|60|40|30|20|17|15|12|10|6)\s*(?:,|/|&|and)?\s*)+)m\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Mode = new(
        @"\b(CW|SSB|RTTY|FT8|FT4|digital|digi|PSK|SSTV|EME|satellites?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IotaRef = new(@"\b(AF|AN|AS|EU|NA|OC|SA)-?(\d{3})\b", RegexOptions.Compiled);

    public static string Bands(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var m = BandRange.Match(text);
        if (m.Success) return $"{m.Groups[1].Value}-{m.Groups[2].Value}m";
        if (Regex.IsMatch(text, @"\bHF\b")) return "HF";
        m = BandList.Match(text);
        return m.Success ? Regex.Replace(m.Groups[1].Value.Trim(), @"\s*(,|/|&|and)\s*|\s+", " ").Trim() + "m" : "";
    }

    public static string Modes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var found = new List<string>();
        foreach (Match m in Mode.Matches(text))
        {
            var v = m.Groups[1].Value.ToUpperInvariant();
            if (v is "DIGITAL" or "DIGI") v = "DIGI";
            if (v.StartsWith("SATELLITE")) v = "SAT";
            if (!found.Contains(v)) found.Add(v);
        }
        return string.Join(" ", found);
    }

    public static string Iota(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var m = IotaRef.Match(text);
        return m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}" : "";
    }

    /// <summary>True se la banda dello spot rientra nelle bande annunciate ("80-10m", "20 15 10m", "HF").</summary>
    public static IEnumerable<string> ExpandBands(string bands)
    {
        if (string.IsNullOrWhiteSpace(bands)) yield break;
        var all = BandPlan.AllBands;
        if (bands == "HF") { foreach (var b in all) if (b != "6m") yield return b; yield break; }
        var m = Regex.Match(bands, @"^(\d+)-(\d+)m$");
        if (m.Success)
        {
            int i = Array.IndexOf(all, m.Groups[1].Value + "m"), j = Array.IndexOf(all, m.Groups[2].Value + "m");
            if (i >= 0 && j >= 0)
                for (int k = Math.Min(i, j); k <= Math.Max(i, j); k++) yield return all[k];
            yield break;
        }
        foreach (Match n in Regex.Matches(bands, @"\d+")) yield return n.Value + "m";
    }
}
