using System.Globalization;
using System.Text.RegularExpressions;
using DXAllert.Core;
using HtmlAgilityPack;

namespace DXAllert.Data.Sources;

/// <summary>425 DX News: calendario HTML https://www.425dxn.org/index.php?op=wcal</summary>
public sealed class Dx425Source : IDxpeditionSource
{
    public const string CalendarUrl = "https://www.425dxn.org/index.php?op=wcal";
    public const string Credit = "425 DX News";
    public string Name => Credit;

    private static readonly string[] MonthNames =
        { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };

    public async Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct) =>
        Parse(await http.GetStringAsync(CalendarUrl, ct), DateTime.UtcNow);

    public static IReadOnlyList<Dxpedition> Parse(string html, DateTime nowUtc)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var list = new List<Dxpedition>();
        var rows = doc.DocumentNode.SelectNodes("//table//tr");
        if (rows is null) return list;

        foreach (var row in rows)
        {
            var cells = row.SelectNodes("./td");
            if (cells is null || cells.Count < 3) continue;
            var title = Clean(cells[1].SelectSingleNode(".//b")?.InnerText);
            if (title.Length == 0 || title == "Operation") continue;
            if (!TryParsePeriod(Clean(cells[0].InnerText), nowUtc, out var start, out var end)) continue;

            // "KH8A: Pago Pago (OC-045), American Samoa"
            var colon = title.IndexOf(':');
            if (colon < 0) continue;
            var place = title[(colon + 1)..].Trim();
            var special = place.StartsWith("special call", StringComparison.OrdinalIgnoreCase);
            var iota = InfoExtractor.Iota(place);
            var entity = special ? "" : Regex.Replace(place.Split(',')[^1], @"\([^)]*\)", "").Trim();
            if (entity.Length == 0 && !special) entity = Regex.Replace(place, @"\([^)]*\)", "").Trim(' ', ',');
            var info = Clean(cells[1].SelectSingleNode(".//code")?.InnerText);
            var bulletin = Clean(cells[2].InnerText);

            foreach (var raw in Regex.Split(title[..colon], @",|\band\b"))
            {
                var call = raw.Trim().ToUpperInvariant();
                if (!CallsignUtil.IsFullCallsign(call)) continue;
                list.Add(new Dxpedition
                {
                    Callsign = call, EntityName = entity, StartUtc = start, EndUtc = end, Iota = iota,
                    Info = info + (bulletin.Length > 0 ? $" [425 DX News #{bulletin}]" : ""),
                    Bands = InfoExtractor.Bands(info), Modes = InfoExtractor.Modes(info),
                    IsSpecialEvent = special, Sources = { Credit },
                });
            }
        }
        return list;
    }

    /// <summary>
    /// Periodi del calendario: "till 08/11", "till 15/01 2027", "07/11-08/11", "15/11-??/01 2027",
    /// "November", "till December", "January 2027", "Jan-Mar 2027".
    /// </summary>
    public static bool TryParsePeriod(string text, DateTime nowUtc, out DateTime? start, out DateTime? end)
    {
        start = end = null;
        var s = Regex.Replace(text, @"\s+", " ").Trim();
        var till = s.StartsWith("till ", StringComparison.OrdinalIgnoreCase);
        if (till) s = s[5..].Trim();

        var year = nowUtc.Year;
        var ym = Regex.Match(s, @"\s(\d{4})$");
        if (ym.Success)
        {
            year = int.Parse(ym.Groups[1].Value);
            s = s[..ym.Index].Trim();
        }

        var parts = s.Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2) return false;
        if (!Token(parts[^1], out var endDay, out var endMonth)) return false;
        end = Utc(year, endMonth, endDay ?? DateTime.DaysInMonth(year, endMonth));
        if (till) return parts.Length == 1;

        if (!Token(parts[0], out var startDay, out var startMonth)) return false;
        var startYear = startMonth > endMonth ? year - 1 : year;
        start = Utc(startYear, startMonth, startDay ?? 1);
        return true;
    }

    // "08/11", "??/01", "November", "Jan"
    private static bool Token(string t, out int? day, out int month)
    {
        day = null;
        month = 0;
        var m = Regex.Match(t, @"^(\d{1,2}|\?\?)/(\d{1,2})$");
        if (m.Success)
        {
            month = int.Parse(m.Groups[2].Value);
            if (month is < 1 or > 12) return false;
            if (m.Groups[1].Value != "??") day = int.Parse(m.Groups[1].Value);
            return day is null or (>= 1 and <= 31);
        }
        if (t.Length < 3) return false;
        month = Array.IndexOf(MonthNames, t[..3].ToLowerInvariant()) + 1;
        return month > 0;
    }

    private static DateTime Utc(int y, int m, int d) =>
        new(y, m, Math.Min(d, DateTime.DaysInMonth(y, m)), 0, 0, 0, DateTimeKind.Utc);

    private static string Clean(string? s) =>
        Regex.Replace(HtmlEntity.DeEntitize(s ?? "").Replace(' ', ' '), @"\s+", " ").Trim();
}
