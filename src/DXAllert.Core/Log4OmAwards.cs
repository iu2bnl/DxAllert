namespace DXAllert.Core;

/// <summary>
/// Diplomi seguiti da Log4OM: il programma di log scrive in ogni QSO i riferimenti (campo
/// APP_L4ONG_QSO_AWARD_REFERENCES). Qui compare un diploma per ogni codice presente nel log, così ogni diploma
/// attivato in Log4OM (anche quelli definiti dall'utente) si vede senza dover essere programmato in DXAllert.
/// </summary>
public static class Log4OmAwards
{
    /// <summary>Nome e, quando è certo, numero totale dei riferimenti dei codici più comuni.</summary>
    private static readonly Dictionary<string, (string Name, string Sponsor, int Total, string Url)> Known = new()
    {
        ["DXCC"] = ("DXCC - entità (conteggio Log4OM)", "ARRL", 0, "https://www.arrl.org/dxcc"),
        ["WAZ"] = ("WAZ - Worked All Zones", "CQ Magazine", 40, ""),
        ["WITUZ"] = ("Worked ITU Zones", "RSGB", 0, ""),
        ["WPX"] = ("WPX - prefissi", "CQ Magazine", 0, ""),
        ["WAC"] = ("WAC - continenti (conteggio Log4OM)", "IARU", 6, ""),
        ["VUCC"] = ("Quadrati locatore (VUCC)", "ARRL", 0, "https://www.arrl.org/vucc"),
        ["FFMA"] = ("FFMA - Fred Fish Memorial Award", "ARRL", 488, "https://www.arrl.org/ffma"),
        ["WAS"] = ("WAS - Worked All States", "ARRL", 50, "https://www.arrl.org/was"),
        ["USA-CA"] = ("USA-CA - contee USA (conteggio Log4OM)", "MARAC", 3077, "https://marac.org/"),
        ["IOTA"] = ("IOTA (conteggio Log4OM)", "IOTA Ltd", 0, "https://www.iota-world.org/"),
        ["CDM"] = ("C.D.M. (conteggio Log4OM)", "ARI", 32, ""),
        ["WAE"] = ("WAE (conteggio Log4OM)", "DARC", 0, ""),
        ["CCC"] = ("CCC (conteggio Log4OM)", "Radio Club Argentino", 0, ""),
        ["MARATHON"] = ("DX Marathon", "CQ Magazine", 0, "https://www.dxmarathon.com/"),
        ["WAIP"] = ("WAIP - Worked All Italian Provinces", "ARI", 0, "https://www.ari.it/"),
        ["WCA"] = ("WCA - World Castles Award", "WCA", 0, "https://wcagroup.org/"),
        ["JCC"] = ("JCC - Japan Century Cities", "JARL", 0, ""),
        ["POTA"] = ("POTA - Parks On The Air", "POTA", 0, "https://pota.app/"),
        ["SOTA"] = ("SOTA - Summits On The Air", "SOTA", 0, "https://www.sota.org.uk/"),
        ["WWFF"] = ("WWFF - World Wide Flora & Fauna", "WWFF", 0, "https://wwff.co/"),
        ["SPPA"] = ("SPPA - SP Powiat Award", "PZK", 0, "https://awards.pzk.org.pl/"),
        ["WANCA"] = ("WANCA - Worked All Norwegian Communes", "NRRL", 0, ""),
        ["DCI"] = ("D.C.I. - Diploma dei Castelli d'Italia", "DCI", 0, ""),
        ["WAJA"] = ("WAJA - Worked All Japan Prefectures", "JARL", 47, ""),
    };

    public static IReadOnlyList<AwardDef> Build(IEnumerable<WorkedEntry> log)
    {
        var codes = log.SelectMany(e => e.Refs).Select(r => r.Award).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        return codes.Select(code =>
        {
            var k = Known.TryGetValue(code, out var info) ? info : (Name: $"{code} (diploma di Log4OM)", Sponsor: "Log4OM", Total: 0, Url: "");
            return new AwardDef
            {
                Id = "l4o-" + code.ToLowerInvariant(),
                Name = k.Name,
                Sponsor = k.Sponsor,
                Group = "Da Log4OM",
                Url = k.Url,
                Rules = $"Riferimenti «{code}» che Log4OM ha assegnato ai QSO del log. Il conteggio dipende da come il diploma è " +
                        "configurato in Log4OM; i diplomi attivati lì compaiono qui al prossimo export e import del file ADIF." +
                        (k.Total > 0 ? $" Riferimenti in tutto: {k.Total}." : ""),
                Key = _ => null,
                Keys = q => q.Entry.Refs.Where(r => r.Award.Equals(code, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.Group.Length > 0 && !r.Group.Equals(r.Ref, StringComparison.OrdinalIgnoreCase) ? $"{r.Ref} · {r.Group}" : r.Ref),
                Levels = k.Total > 0 ? new[] { new AwardLevel("Tutti", k.Total) } : Array.Empty<AwardLevel>(),
            };
        }).ToList();
    }
}
