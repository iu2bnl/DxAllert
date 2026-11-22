using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Prefettura giapponese di un QSO, per il WAJA (JARL): numerazione JARL 01 Hokkaido … 47 Okinawa.</summary>
public static class JapanPrefectures
{
    /// <summary>Codice JARL a due cifre, o null se il log non permette di saperlo.
    /// Ordine: campo STATE (ADIF usa la numerazione JARL), poi il riferimento JCC di Log4OM (le prime due cifre
    /// sono la prefettura), poi il distretto 8 del nominativo, che è solo Hokkaido.</summary>
    public static string? Of(WorkedEntry e)
    {
        if (int.TryParse(e.State.Trim(), out var n) && n is >= 1 and <= 47) return n.ToString("00");
        foreach (var r in e.Refs)
            if (r.Award is "JCC" or "JCG" && r.Ref.Length >= 4 && int.TryParse(r.Ref[..2], out var p) && p is >= 1 and <= 47)
                return p.ToString("00");
        var call = Callsign(e.Callsign);
        if (Regex.IsMatch(call, "^J[A-S]8[A-Z]")) return "01";
        return null;
    }

    private static string Callsign(string call) =>
        call.ToUpperInvariant().Split('/').OrderByDescending(p => p.Length).First();
}

/// <summary>Provincia cinese di un QSO: le 31 province e regioni della Cina continentale più Taiwan, Hong Kong e Macao.</summary>
public static class ChinaProvinces
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>
    {
        "AH", "BJ", "CQ", "FJ", "GD", "GS", "GX", "GZ", "HA", "HB", "HE", "HI", "HL", "HN", "JL", "JS", "JX", "LN",
        "NM", "NX", "QH", "SC", "SD", "SH", "SN", "SX", "TJ", "XJ", "XZ", "YN", "ZJ", "TW", "HK", "MO",
    };

    // Distretto (cifra del nominativo) e prima lettera del suffisso -> provincia, secondo lo schema dei nominativi
    // cinesi. Tabella non verificata su un documento ufficiale: le province ricavate così sono segnalate come stima.
    private static readonly Dictionary<char, (char From, char To, string Code)[]> Areas = new()
    {
        ['1'] = new[] { ('A', 'Z', "BJ") },
        ['2'] = new[] { ('A', 'H', "HL"), ('I', 'P', "JL"), ('Q', 'X', "LN") },
        ['3'] = new[] { ('A', 'F', "TJ"), ('G', 'L', "NM"), ('M', 'R', "HE"), ('S', 'X', "SX") },
        ['4'] = new[] { ('A', 'H', "SH"), ('I', 'P', "SD"), ('Q', 'X', "JS") },
        ['5'] = new[] { ('A', 'H', "ZJ"), ('I', 'P', "JX"), ('Q', 'X', "FJ") },
        ['6'] = new[] { ('A', 'H', "AH"), ('I', 'P', "HA"), ('Q', 'X', "HB") },
        ['7'] = new[] { ('A', 'H', "HN"), ('I', 'P', "GD"), ('Q', 'X', "GX"), ('Y', 'Z', "HI") },
        ['8'] = new[] { ('A', 'F', "SC"), ('G', 'L', "CQ"), ('M', 'R', "GZ"), ('S', 'X', "YN") },
        ['9'] = new[] { ('A', 'F', "SN"), ('G', 'L', "GS"), ('M', 'R', "NX"), ('S', 'X', "QH") },
        ['0'] = new[] { ('A', 'F', "XJ"), ('G', 'L', "XZ") },
    };

    private static readonly Regex ChinaCall = new(@"^B[A-Z]?(\d)([A-Z])[A-Z]*$", RegexOptions.Compiled);

    /// <summary>Provincia e se è stata ricavata dal solo nominativo (stima). Null se non si può sapere.</summary>
    public static (string Code, bool FromCall)? Of(WorkedEntry e, string? entity)
    {
        switch (entity)
        {
            case "Taiwan": case "Pratas Island": return ("TW", false);
            case "Hong Kong": return ("HK", false);
            case "Macao": return ("MO", false);
            case "China": break;
            default: return null;
        }
        var state = e.State.Trim().ToUpperInvariant();
        if (Codes.Contains(state)) return (state, false);
        foreach (var r in e.Refs)
            if (r.Award is "WAPC" or "CNPA" or "CHINA" && Codes.Contains(r.Ref.ToUpperInvariant())) return (r.Ref.ToUpperInvariant(), false);

        var call = e.Callsign.ToUpperInvariant().Split('/').OrderByDescending(p => p.Length).First();
        var m = ChinaCall.Match(call);
        if (!m.Success || !Areas.TryGetValue(m.Groups[1].Value[0], out var ranges)) return null;
        var letter = m.Groups[2].Value[0];
        foreach (var (from, to, code) in ranges)
            if (letter >= from && letter <= to) return (code, true);
        return null;
    }
}
