using System.Globalization;
using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Parser del formato ADIF (Amateur Data Interchange Format).</summary>
public static class AdifParser
{
    // <fieldname:length> o <fieldname:length:type>  seguito dal valore (senza '<')
    private static readonly Regex FieldRx = new(
        @"<([A-Za-z_][A-Za-z_0-9]*):\d+(?::[^>]*)?>([^<]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<WorkedEntry> Parse(string content)
    {
        var results = new List<WorkedEntry>();

        // Salta l'intestazione ADIF fino a <EOH>
        var eoh = content.IndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
        var body = eoh >= 0 ? content[(eoh + 5)..] : content;

        foreach (var record in Regex.Split(body, @"<EOR>", RegexOptions.IgnoreCase))
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in FieldRx.Matches(record))
                fields[m.Groups[1].Value] = m.Groups[2].Value.Trim();

            var call = fields.GetValueOrDefault("CALL", "").ToUpperInvariant();
            if (call.Length == 0) continue;

            // SUBMODE ha la precedenza su MODE per i modi digitali (es. FT8 dentro DIGI)
            var mode = (fields.ContainsKey("SUBMODE")
                ? fields["SUBMODE"]
                : fields.GetValueOrDefault("MODE", "")).ToUpperInvariant();
            if (mode.Length == 0) continue;

            // BAND diretto; se assente si ricava da FREQ (in MHz)
            var band = fields.GetValueOrDefault("BAND", "").ToLowerInvariant();
            if (band.Length == 0 && fields.TryGetValue("FREQ", out var freqStr) &&
                double.TryParse(freqStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz) && mhz > 0)
                band = BandPlan.BandOf(mhz * 1000);
            if (band.Length == 0) continue;

            var country = fields.GetValueOrDefault("COUNTRY",
                          fields.GetValueOrDefault("DXCC_ENTITY_NAME", ""));

            // CNTY: "MA,Middlesex" (formato ADIF), a volte "MA, Middlesex" o solo "Middlesex" con lo stato in STATE.
            var state = fields.GetValueOrDefault("STATE", "").ToUpperInvariant();
            var county = fields.GetValueOrDefault("CNTY", "");
            var comma = county.IndexOf(',');
            if (comma >= 0)
            {
                if (state.Length == 0) state = county[..comma].Trim().ToUpperInvariant();
                county = county[(comma + 1)..].Trim();
            }

            results.Add(new WorkedEntry(call, band, mode, country, state, county));
        }
        return results;
    }
}
