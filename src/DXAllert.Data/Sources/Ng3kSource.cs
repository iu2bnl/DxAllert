using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DXAllert.Core;

namespace DXAllert.Data.Sources;

/// <summary>NG3K Announced DX Operations: feed RSS https://www.ng3k.com/adxo.xml</summary>
public sealed class Ng3kSource : IDxpeditionSource
{
    public const string FeedUrl = "https://www.ng3k.com/adxo.xml";
    public string Name => "NG3K";

    // "Oct 3-Nov 20, 2026" | "Nov 3-16, 2026" | "Dec 12, 2026-Jan 2, 2027" | "Nov 15, 2026"
    private static readonly Regex Dates = new(
        @"^([A-Za-z]{3})\w* (\d{1,2})(?:, (\d{4}))?(?:\s*-\s*(?:([A-Za-z]{3})\w* )?(\d{1,2}))?, (\d{4})$",
        RegexOptions.Compiled);

    public async Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct) =>
        Parse(await http.GetStringAsync(FeedUrl, ct));

    public static IReadOnlyList<Dxpedition> Parse(string xml)
    {
        var list = new List<Dxpedition>();
        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            // "Solomon Is: Oct 3-Nov 20, 2026 -- H44MS -- QSL via: LoTW"
            var title = ((string?)item.Element("title") ?? "").Trim();
            var parts = title.Split(" -- ");
            if (parts.Length < 2) continue;
            var colon = parts[0].IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0) continue;
            if (!TryParseDates(parts[0][(colon + 2)..].Trim(), out var start, out var end)) continue;

            var descr = ((string?)item.Element("description") ?? "").Split("--");
            var info = descr.Length >= 6 ? Regex.Replace(descr[^1], @"\s+", " ").Trim() : "";
            var link = ((string?)item.Element("link") ?? "").Trim();
            var qsl = parts.Length > 2 ? Regex.Replace(parts[2], @"^QSL via:\s*", "").Trim() : "";
            foreach (var call in Callsigns(parts[1].Trim().ToUpperInvariant(), info))
                list.Add(new Dxpedition
                {
                    Callsign = call, EntityName = parts[0][..colon].Trim(), StartUtc = start, EndUtc = end,
                    QslVia = qsl == "?" ? "" : qsl, Url = link.Contains("adxo.html") ? "" : link, Info = info,
                    Bands = InfoExtractor.Bands(info), Modes = InfoExtractor.Modes(info), Iota = InfoExtractor.Iota(info),
                    Sources = { "NG3K" },
                });
        }
        return list;
    }

    /// <summary>
    /// Molte operazioni sono elencate col solo prefisso ("3B9", "ZD7"): i nominativi veri sono nel
    /// testo ("By M0CFW as 3B9/M0CFW", "By G4BWP as ZD7WP and 5Z4VJ as ZD7VJ", "By 3B8/E70A").
    /// </summary>
    public static IReadOnlyList<string> Callsigns(string listed, string info)
    {
        var found = Regex.Matches(info.Split(';')[0], @"[A-Z0-9]+(?:/[A-Z0-9]+)*")
            .Select(m => m.Value)
            .Where(c => c != listed && CallsignUtil.IsFullCallsign(c) &&
                        (c.StartsWith(listed, StringComparison.Ordinal) || c.EndsWith("/" + listed, StringComparison.Ordinal)))
            .Distinct().ToList();
        if (found.Count > 0) return found;
        if (CallsignUtil.IsFullCallsign(listed)) return new[] { listed };
        // "By DL1MGB" senza altro: il nominativo sarà prefisso/nominativo di casa.
        var by = Regex.Match(info, @"^By ([A-Z0-9]+)\s*(?:;|fm\b|$)");
        return by.Success && CallsignUtil.IsFullCallsign(by.Groups[1].Value)
            ? new[] { $"{listed}/{by.Groups[1].Value}" }
            : new[] { listed };
    }

    public static bool TryParseDates(string s, out DateTime start, out DateTime end)
    {
        start = end = default;
        var m = Dates.Match(s);
        if (!m.Success) return false;
        var endYear = int.Parse(m.Groups[6].Value);
        if (!Month(m.Groups[1].Value, out var m1)) return false;
        var m2 = m1;
        if (m.Groups[4].Success && !Month(m.Groups[4].Value, out m2)) return false;
        var d1 = int.Parse(m.Groups[2].Value);
        var d2 = m.Groups[5].Success ? int.Parse(m.Groups[5].Value) : d1;
        var startYear = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : (m1 > m2 ? endYear - 1 : endYear);
        try
        {
            start = new DateTime(startYear, m1, d1, 0, 0, 0, DateTimeKind.Utc);
            end = new DateTime(endYear, m2, d2, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool Month(string abbr, out int month)
    {
        var ok = DateTime.TryParseExact(abbr[..3], "MMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
        month = d.Month;
        return ok;
    }
}
