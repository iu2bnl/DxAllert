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
            // Log4OM: "FL,OSCEOLA // Osceola": dopo "//" ripete il nome, si tiene la prima parte.
            var slashes = county.IndexOf("//", StringComparison.Ordinal);
            if (slashes >= 0) county = county[..slashes].Trim();

            DateTime? date = DateTime.TryParseExact(fields.GetValueOrDefault("QSO_DATE", ""), "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            var qsl = QslFlags.None;
            if (IsYes(fields, "QSL_RCVD")) qsl |= QslFlags.Card;
            if (IsYes(fields, "LOTW_QSL_RCVD")) qsl |= QslFlags.Lotw;
            if (IsYes(fields, "EQSL_QSL_RCVD")) qsl |= QslFlags.Eqsl;
            var cq = int.TryParse(fields.GetValueOrDefault("CQZ", ""), out var z1) ? z1 : 0;
            var itu = int.TryParse(fields.GetValueOrDefault("ITUZ", ""), out var z2) ? z2 : 0;
            var iota = fields.GetValueOrDefault("IOTA", "").ToUpperInvariant();
            var grid = fields.GetValueOrDefault("GRIDSQUARE", "").ToUpperInvariant();
            var refs = Log4OmRefs(fields.GetValueOrDefault("APP_L4ONG_QSO_AWARD_REFERENCES", ""));

            results.Add(new WorkedEntry(call, band, mode, country, state, county, date, qsl, cq, itu, iota, grid, refs));
        }
        return results;
    }

    /// <summary>Riferimenti dei diplomi di Log4OM: [{"AC":"WAIP","R":"RN","G":"EMILIA-ROMAGNA","S":"CONFIRMED"}, …].</summary>
    private static string Log4OmRefs(string json)
    {
        if (json.Length == 0 || json[0] != '[') return "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var refs = new List<AwardRef>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string Get(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
                var ac = Get("AC").ToUpperInvariant();
                var r = Get("R");
                // DXCC_QSL ripete il DXCC solo per le conferme.
                if (ac.Length == 0 || r.Length == 0 || ac == "DXCC_QSL") continue;
                refs.Add(new AwardRef(ac, r, Get("G"), Get("S").ToUpperInvariant()));
            }
            return AwardRef.Pack(refs);
        }
        catch (System.Text.Json.JsonException) { return ""; }
    }

    // "Y" (ricevuta) o "V" (verificata) valgono come conferma.
    private static bool IsYes(Dictionary<string, string> f, string key) =>
        f.TryGetValue(key, out var v) && v.Length > 0 && (char.ToUpperInvariant(v[0]) is 'Y' or 'V');
}
