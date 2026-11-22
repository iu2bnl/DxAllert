using System.Globalization;
using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Chi organizza il contest: Italia (ARI e club italiani), Europa (associazioni europee, IARU Regione 1), Mondo (CQ, ARRL, JARL, ...).</summary>
public enum ContestRegion { Italia, Europa, Mondo, Altro }

/// <summary>Un'edizione di un contest, con orari UTC.</summary>
public sealed record ContestEvent(
    string Name, ContestRegion Region, string Modes, string Bands, string Url,
    DateTime StartUtc, DateTime EndUtc, bool FromWeeklyFeed);

/// <summary>
/// Un contest ricorrente e la regola con cui cade ogni anno. "Fine settimana completo": sabato e
/// domenica nello stesso mese; l'ennesimo (o l'ultimo, N = -1) si conta dai sabati.
/// </summary>
public sealed class ContestDef
{
    public enum Kind { Weekend, Weekday, Fixed }

    public required string Name { get; init; }
    public required ContestRegion Region { get; init; }
    public string Modes { get; init; } = "";
    public string Bands { get; init; } = "";
    public string Url { get; init; } = "";
    public Kind Rule { get; init; } = Kind.Weekend;
    /// <summary>Mese (1-12); 0 = tutti i mesi.</summary>
    public int Month { get; init; }
    /// <summary>1 = primo, 2 = secondo, ...; -1 = ultimo.</summary>
    public int N { get; init; } = 1;
    public DayOfWeek Day { get; init; } = DayOfWeek.Saturday;
    /// <summary>Inizio rispetto alle 00:00 UTC del giorno di riferimento (il sabato per i fine settimana).</summary>
    public TimeSpan Start { get; init; }
    /// <summary>Se impostato, inizio da novembre a marzo (orari invernali dei contest VHF italiani).</summary>
    public TimeSpan? WinterStart { get; init; }
    public TimeSpan Duration { get; init; }
    /// <summary>Per Kind.Fixed: date di inizio già comprensive dell'ora UTC.</summary>
    public DateTime[] Dates { get; init; } = Array.Empty<DateTime>();

    public IEnumerable<ContestEvent> Occurrences(int year)
    {
        if (Rule == Kind.Fixed)
        {
            foreach (var d in Dates.Where(d => d.Year == year))
                yield return Make(d);
            yield break;
        }
        var months = Month == 0 ? Enumerable.Range(1, 12) : new[] { Month };
        foreach (var m in months)
        {
            var anchor = Rule == Kind.Weekend ? NthFullWeekend(year, m, N) : NthWeekday(year, m, Day, N);
            if (anchor is not { } a) continue;
            var start = WinterStart is { } w && (m >= 11 || m <= 3) ? w : Start;
            yield return Make(a + start);
        }
    }

    private ContestEvent Make(DateTime startUtc) =>
        new(Name, Region, Modes, Bands, Url, DateTime.SpecifyKind(startUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(startUtc + Duration, DateTimeKind.Utc), false);

    /// <summary>Il sabato dell'ennesimo fine settimana completo del mese.</summary>
    public static DateTime? NthFullWeekend(int year, int month, int n)
    {
        var saturdays = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(d => new DateTime(year, month, d))
            .Where(d => d.DayOfWeek == DayOfWeek.Saturday && d.AddDays(1).Month == month)
            .ToList();
        return Pick(saturdays, n);
    }

    public static DateTime? NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var days = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
            .Select(d => new DateTime(year, month, d)).Where(d => d.DayOfWeek == day).ToList();
        return Pick(days, n);
    }

    private static DateTime? Pick(List<DateTime> list, int n) =>
        n == -1 ? list.LastOrDefault() is { Year: > 1 } last ? last : null
        : n >= 1 && n <= list.Count ? list[n - 1] : null;
}

public static class ContestCatalog
{
    private static TimeSpan H(double hours) => TimeSpan.FromHours(hours);

    private static ContestDef W(string name, ContestRegion region, int month, int n, double startH, double hours,
                                string modes, string bands, string url = "") => new()
    {
        Name = name, Region = region, Month = month, N = n, Start = H(startH), Duration = H(hours),
        Modes = modes, Bands = bands, Url = url,
    };

    private const string Hf = "160-10m";
    private const string HfNoWarc = "160-10m (no WARC)";

    /// <summary>
    /// I contest principali con la loro regola abituale. Le date vanno sempre confrontate con il
    /// regolamento dell'anno: gli organizzatori possono cambiarle.
    /// </summary>
    public static readonly IReadOnlyList<ContestDef> All = new List<ContestDef>
    {
        // ------------------------------------------------ Italia (regolamenti ARI 2026)
        W("ARI International DX Contest", ContestRegion.Italia, 5, 1, 12, 24, "CW SSB RTTY", HfNoWarc, "https://www.ari.it/contest-hf/ari-international.html"),
        W("Contest Sezioni ARI HF", ContestRegion.Italia, 6, 2, 12, 24, "CW SSB RTTY", HfNoWarc, "https://www.ari.it/contest-hf/contest-sezioni.html"),
        W("Contest ARI 40/80", ContestRegion.Italia, 12, 2, 13, 24, "CW SSB RTTY", "40m 80m", "https://www.ari.it/contest-hf/contest-4080.html"),
        W("Trofeo ARI Città di Livorno", ContestRegion.Italia, 3, 1, 14, 24, "CW SSB", "144 MHz e sup.", "https://www.ari.it/contest-vhf-up/trofei-ari/8651.html"),
        W("Trofeo ARI Città di Rovigo", ContestRegion.Italia, 5, 1, 14, 24, "CW SSB", "144 MHz e sup.", "https://www.ari.it/contest-vhf-up/trofei-ari/8651.html"),
        W("Trofeo ARI Canavese", ContestRegion.Italia, 6, 1, 14, 24, "CW SSB", "1,3 GHz e sup.", "https://www.ari.it/contest-vhf-up/trofei-ari/8651.html"),
        W("Trofeo ARI Lario", ContestRegion.Italia, 7, 1, 14, 24, "CW SSB", "144 MHz e sup.", "https://www.ari.it/contest-vhf-up/trofei-ari/8651.html"),
        W("Marconi Memorial Contest 144 MHz CW", ContestRegion.Italia, 11, 1, 14, 24, "CW", "144 MHz", "https://www.ari.it/contest-vhf-up/trofei-ari/8651.html"),
        W("Alpe Adria VHF Contest", ContestRegion.Italia, 8, 1, 24 + 6, 8, "CW SSB", "144 MHz", "https://www.ari.it/contest-vhf-up/altri/8770.html"),
        Iac("I.A.C. 144 MHz", DayOfWeek.Tuesday, 1, "144 MHz"),
        Iac("I.A.C. 432 MHz", DayOfWeek.Tuesday, 2, "432 MHz"),
        Iac("I.A.C. 1296 MHz", DayOfWeek.Tuesday, 3, "1296 MHz"),
        Iac("I.A.C. 2,3 GHz e sup.", DayOfWeek.Tuesday, 4, "2,3 GHz e sup."),
        Iac("I.A.C. 50 MHz", DayOfWeek.Thursday, 2, "50 MHz"),
        Iac("I.A.C. 70 MHz", DayOfWeek.Thursday, 3, "70 MHz"),
        Ft8("ARI FT8 Activity 144 MHz", 1, "144 MHz"),
        Ft8("ARI FT8 Activity 432 MHz", 2, "432 MHz"),
        Fixed("Contest delle Sezioni ARI 144 MHz", "CW SSB", "144 MHz", "https://www.ari.it/contest-vhf-up/contest-delle-sezioni-v-up.html", 6, new DateTime(2026, 3, 15, 9, 0, 0)),
        Fixed("Contest delle Sezioni ARI 432 MHz e sup.", "CW SSB", "432 MHz e sup.", "https://www.ari.it/contest-vhf-up/contest-delle-sezioni-v-up.html", 4, new DateTime(2026, 4, 12, 8, 0, 0)),
        Fixed("Contest Lazio 144 MHz", "CW SSB", "144 MHz", "https://www.ari.it/contest-vhf-up/altri/8660.html", 4, new DateTime(2026, 4, 19, 8, 0, 0)),
        Fixed("Contest Veneto 432/1296 MHz", "CW SSB", "432 1296 MHz", "https://www.ari.it/contest-vhf-up/altri/8699.html", 5, new DateTime(2026, 5, 9, 11, 0, 0)),
        Fixed("Contest Veneto 144 MHz", "CW SSB", "144 MHz", "https://www.ari.it/contest-vhf-up/altri/8699.html", 4, new DateTime(2026, 5, 10, 7, 0, 0)),

        // ------------------------------------------------ Europa
        W("WAE DX Contest, CW", ContestRegion.Europa, 8, 2, 0, 48, "CW", "80-10m (no WARC)", "https://www.darc.de/der-club/referate/conteste/wae-dx-contest/en/"),
        W("WAE DX Contest, SSB", ContestRegion.Europa, 9, 2, 0, 48, "SSB", "80-10m (no WARC)", "https://www.darc.de/der-club/referate/conteste/wae-dx-contest/en/"),
        W("WAE DX Contest, RTTY", ContestRegion.Europa, 11, 2, 0, 48, "RTTY", "80-10m (no WARC)", "https://www.darc.de/der-club/referate/conteste/wae-dx-contest/en/"),
        W("Worked All Germany Contest", ContestRegion.Europa, 10, 3, 15, 24, "CW SSB", "80-10m (no WARC)", "https://www.darc.de/der-club/referate/conteste/wag-contest/en/"),
        W("Russian DX Contest", ContestRegion.Europa, 3, 3, 12, 24, "CW SSB", HfNoWarc, "https://www.rdxc.org"),
        W("UBA DX Contest, SSB", ContestRegion.Europa, 1, -1, 13, 24, "SSB", "80-10m (no WARC)", "https://www.uba.be/en/hf/contest-rules"),
        W("UBA DX Contest, CW", ContestRegion.Europa, 2, -1, 13, 24, "CW", "80-10m (no WARC)", "https://www.uba.be/en/hf/contest-rules"),
        W("PACC Contest", ContestRegion.Europa, 2, 2, 12, 24, "CW SSB", HfNoWarc, "https://www.veron.nl"),
        W("HA DX Contest", ContestRegion.Europa, 1, 3, 12, 24, "CW SSB", HfNoWarc),
        W("SP DX Contest", ContestRegion.Europa, 4, 1, 12, 24, "CW SSB", HfNoWarc, "https://spdxcontest.pzk.pl"),
        W("Helvetia Contest", ContestRegion.Europa, 4, -1, 13, 24, "CW SSB", HfNoWarc, "https://www.uska.ch"),
        W("EU HF Championship", ContestRegion.Europa, 8, 1, 12, 12, "CW SSB", HfNoWarc),
        W("LZ DX Contest", ContestRegion.Europa, 11, 3, 12, 24, "CW SSB", "80-10m (no WARC)", "http://lzdx.bfra.bg/rulesen.html"),
        W("OK/OM DX Contest, CW", ContestRegion.Europa, 11, 2, 12, 24, "CW", HfNoWarc),
        W("Croatian DX Contest", ContestRegion.Europa, 12, 3, 14, 24, "CW", HfNoWarc),
        W("Scandinavian Activity Contest, CW", ContestRegion.Europa, 9, 3, 12, 24, "CW", "80-10m (no WARC)", "https://www.sactest.net"),
        W("Scandinavian Activity Contest, SSB", ContestRegion.Europa, 10, 2, 12, 24, "SSB", "80-10m (no WARC)", "https://www.sactest.net"),
        W("RSGB IOTA Contest", ContestRegion.Europa, 7, -1, 12, 24, "CW SSB", "80-10m (no WARC)", "https://www.rsgbiota.org"),
        W("IARU Region 1 Field Day, CW", ContestRegion.Europa, 6, 1, 13, 24, "CW", HfNoWarc, "https://www.iaru-r1.org"),
        W("IARU Region 1 Field Day, SSB", ContestRegion.Europa, 9, 1, 13, 24, "SSB", HfNoWarc, "https://www.iaru-r1.org"),
        W("IARU Region 1 VHF Contest", ContestRegion.Europa, 9, 1, 14, 24, "CW SSB", "144 MHz", "https://www.iaru-r1.org"),
        W("IARU Region 1 UHF/Microwave Contest", ContestRegion.Europa, 10, 1, 14, 24, "CW SSB", "432 MHz e sup.", "https://www.iaru-r1.org"),

        // ------------------------------------------------ Mondo
        W("CQ Worldwide DX Contest, SSB", ContestRegion.Mondo, 10, -1, 0, 48, "SSB", HfNoWarc, "https://www.cqww.com/rules.htm"),
        W("CQ Worldwide DX Contest, CW", ContestRegion.Mondo, 11, -1, 0, 48, "CW", HfNoWarc, "https://www.cqww.com/rules.htm"),
        W("CQ Worldwide DX Contest, RTTY", ContestRegion.Mondo, 9, -1, 0, 48, "RTTY", "80-10m (no WARC)", "https://www.cqwwrtty.com/rules.htm"),
        W("CQ WW WPX Contest, SSB", ContestRegion.Mondo, 3, -1, 0, 48, "SSB", HfNoWarc, "https://www.cqwpx.com/rules.htm"),
        W("CQ WW WPX Contest, CW", ContestRegion.Mondo, 5, -1, 0, 48, "CW", HfNoWarc, "https://www.cqwpx.com/rules.htm"),
        W("CQ WW WPX Contest, RTTY", ContestRegion.Mondo, 2, 2, 0, 48, "RTTY", "80-10m (no WARC)", "https://www.cqwpxrtty.com/rules.htm"),
        W("CQ 160-Meter Contest, CW", ContestRegion.Mondo, 1, -1, -2, 48, "CW", "160m", "https://www.cq160.com/rules.htm"),
        W("CQ 160-Meter Contest, SSB", ContestRegion.Mondo, 2, -1, -2, 48, "SSB", "160m", "https://www.cq160.com/rules.htm"),
        W("CQ Worldwide VHF Contest", ContestRegion.Mondo, 7, 3, 18, 27, "Tutti", "50 144 MHz", "https://www.cqww-vhf.com/rules.htm"),
        W("ARRL International DX Contest, CW", ContestRegion.Mondo, 2, 3, 0, 48, "CW", HfNoWarc, "https://www.arrl.org/arrl-dx"),
        W("ARRL International DX Contest, SSB", ContestRegion.Mondo, 3, 1, 0, 48, "SSB", HfNoWarc, "https://www.arrl.org/arrl-dx"),
        W("ARRL 10-Meter Contest", ContestRegion.Mondo, 12, 2, 0, 48, "CW SSB", "10m", "https://www.arrl.org/10-meter"),
        W("ARRL 160-Meter Contest", ContestRegion.Mondo, 12, 1, -2, 42, "CW", "160m", "https://www.arrl.org/160-meter"),
        W("IARU HF World Championship", ContestRegion.Mondo, 7, 2, 12, 24, "CW SSB", HfNoWarc, "https://www.arrl.org/iaru-hf-championship"),
        W("All Asian DX Contest, CW", ContestRegion.Mondo, 6, 3, 0, 48, "CW", HfNoWarc, "https://www.jarl.org/English/"),
        W("All Asian DX Contest, SSB", ContestRegion.Mondo, 9, 1, 0, 48, "SSB", HfNoWarc, "https://www.jarl.org/English/"),
        W("JIDX CW Contest", ContestRegion.Mondo, 4, 2, 7, 30, "CW", HfNoWarc, "https://www.jidx.org/jidxrule-e.html"),
        W("JIDX SSB Contest", ContestRegion.Mondo, 11, 2, 7, 30, "SSB", HfNoWarc, "https://www.jidx.org/jidxrule-e.html"),
        W("World Wide Digi DX Contest", ContestRegion.Mondo, 8, -1, 12, 24, "FT4 FT8", "160-6m (no WARC)", "https://ww-digi.com"),
    };

    // Italian Activity Contest: una sera al mese per banda; orari invernali da novembre a marzo.
    private static ContestDef Iac(string name, DayOfWeek day, int n, string band) => new()
    {
        Name = name, Region = ContestRegion.Italia, Rule = ContestDef.Kind.Weekday, Month = 0, Day = day, N = n,
        Start = H(17), WinterStart = H(18), Duration = H(4), Modes = "CW SSB", Bands = band,
        Url = "https://www.ari.it/contest-vhf-up/iac/regolamento.html",
    };

    private static ContestDef Ft8(string name, int n, string band) => new()
    {
        Name = name, Region = ContestRegion.Italia, Rule = ContestDef.Kind.Weekday, Month = 0, Day = DayOfWeek.Wednesday, N = n,
        Start = H(17), WinterStart = H(18), Duration = H(3), Modes = "FT8", Bands = band,
        Url = "https://www.ari.it/contest-vhf-up/ft8-activity.html",
    };

    // Contest con le sole date dell'edizione annunciata (le regole non danno una ricorrenza).
    private static ContestDef Fixed(string name, string modes, string bands, string url, double hours, params DateTime[] starts) => new()
    {
        Name = name, Region = ContestRegion.Italia, Rule = ContestDef.Kind.Fixed, Dates = starts, Duration = H(hours),
        Modes = modes, Bands = bands, Url = url,
    };

    /// <summary>Tutte le edizioni che toccano l'intervallo [from, to).</summary>
    public static List<ContestEvent> Between(DateTime fromUtc, DateTime toUtc) =>
        Enumerable.Range(fromUtc.Year - 1, toUtc.Year - fromUtc.Year + 2)
            .SelectMany(y => All.SelectMany(c => c.Occurrences(y)))
            .Where(e => e.EndUtc > fromUtc && e.StartUtc < toUtc)
            .OrderBy(e => e.StartUtc).ThenBy(e => e.Name)
            .ToList();

    private static readonly HashSet<string> Noise = new() { "contest", "dx", "the", "de", "of", "and", "test", "worldwide", "world", "wide", "ww" };

    private static readonly HashSet<string> Modes = new() { "cw", "ssb", "phone", "rtty", "digi", "digital", "ft8", "ft4", "psk" };

    private static bool Differ(HashSet<string> a, HashSet<string> b, Func<string, bool> kind)
    {
        var ka = a.Where(kind).ToHashSet(); var kb = b.Where(kind).ToHashSet();
        return ka.Count > 0 && kb.Count > 0 && !ka.SetEquals(kb);
    }

    private static HashSet<string> Words(string name) =>
        Regex.Matches(name.ToLowerInvariant(), "[a-z0-9]+").Select(m => m.Value).Where(w => !Noise.Contains(w)).ToHashSet();

    /// <summary>Stesso contest con nomi scritti diversamente (es. dal feed WA7BNM): almeno due parole significative in comune, o tutte se il nome è corto.</summary>
    public static bool SameName(string a, string b)
    {
        var wa = Words(a); var wb = Words(b);
        // Numeri (10 / 160 metri) e modi diversi indicano contest diversi anche se il resto del nome coincide.
        if (Differ(wa, wb, w => w.All(char.IsDigit)) || Differ(wa, wb, Modes.Contains)) return false;
        var common = wa.Intersect(wb).Count();
        return common >= 2 || (common >= 1 && common == Math.Min(wa.Count, wb.Count));
    }

    /// <summary>
    /// Area del contest secondo il catalogo, quando il nome corrisponde senza dubbi (senza date a
    /// confermarlo, tutte le parole del nome più corto devono comparire nell'altro); altrimenti Altro.
    /// </summary>
    public static (ContestRegion Region, string Url, string Modes, string Bands) Lookup(string name)
    {
        var w = Words(name);
        var def = All.FirstOrDefault(c =>
        {
            var wc = Words(c.Name);
            var (small, big) = wc.Count <= w.Count ? (wc, w) : (w, wc);
            return small.Count > 0 && small.IsSubsetOf(big) && (small.Count >= 2 || wc.SetEquals(w)) && SameName(c.Name, name);
        });
        return def is null ? (ContestRegion.Altro, "", "", "") : (def.Region, def.Url, def.Modes, def.Bands);
    }

    /// <summary>
    /// Unisce il catalogo con gli eventi della settimana del feed WA7BNM: dove si sovrappongono vale il
    /// feed (orari esatti dell'edizione), con area, modi e link presi dal catalogo.
    /// </summary>
    public static List<ContestEvent> Merge(List<ContestEvent> catalog, IEnumerable<IcsEvent> weekly)
    {
        var result = new List<ContestEvent>(catalog);
        foreach (var w in weekly)
        {
            var match = result.FirstOrDefault(c => !c.FromWeeklyFeed && SameName(c.Name, w.Summary) &&
                                                   c.StartUtc < w.EndUtc.AddHours(12) && w.StartUtc < c.EndUtc.AddHours(12));
            var (region, url, modes, bands) = match is not null ? (match.Region, match.Url, match.Modes, match.Bands) : Lookup(w.Summary);
            if (match is not null) result.Remove(match);
            result.Add(new ContestEvent(w.Summary, region, modes, bands, url.Length > 0 ? url : w.Url, w.StartUtc, w.EndUtc, true));
        }
        return result.OrderBy(e => e.StartUtc).ThenBy(e => e.Name).ToList();
    }
}

public sealed record IcsEvent(string Summary, DateTime StartUtc, DateTime EndUtc, string Url);

/// <summary>Lettore minimo di file iCalendar (VEVENT con DTSTART/DTEND in UTC).</summary>
public static class IcsParser
{
    public static List<IcsEvent> Parse(string text)
    {
        // Le righe lunghe proseguono sulla riga dopo, che inizia con uno spazio o una tabulazione.
        var unfolded = Regex.Replace(text.Replace("\r\n", "\n"), "\n[ \t]", "");
        var list = new List<IcsEvent>();
        Dictionary<string, string>? ev = null;
        foreach (var line in unfolded.Split('\n'))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) { ev = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (ev is not null && ev.TryGetValue("SUMMARY", out var s) &&
                    ev.TryGetValue("DTSTART", out var a) && TryDate(a, out var start))
                {
                    var end = ev.TryGetValue("DTEND", out var b) && TryDate(b, out var e) ? e : start;
                    list.Add(new IcsEvent(Unescape(s), start, end, ev.GetValueOrDefault("URL", "").Trim()));
                }
                ev = null;
                continue;
            }
            if (ev is null) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Split(';')[0].Trim();
            ev[key] = line[(colon + 1)..].Trim();
        }
        return list;
    }

    private static bool TryDate(string v, out DateTime utc)
    {
        var ok = DateTime.TryParseExact(v.TrimEnd('Z'), new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d);
        utc = DateTime.SpecifyKind(d, DateTimeKind.Utc);
        return ok;
    }

    private static string Unescape(string s) =>
        s.Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ").Replace("\\N", " ").Replace("\\\\", "\\").Trim();
}
