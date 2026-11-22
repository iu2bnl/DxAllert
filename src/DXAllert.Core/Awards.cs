using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>Un QSO pronto per i diplomi: entità DXCC (e WAE) già ricavate dal nominativo.</summary>
public sealed record AwardQso(WorkedEntry Entry, DxccEntity? Dxcc, DxccEntity? Wae, DxccEntity? Home)
{
    public string Call => Entry.Callsign;
    public string Band => Entry.Band;

    /// <summary>Zona CQ: dal log se c'è, altrimenti dal cty.dat.</summary>
    public int CqZone => Entry.CqZone > 0 ? Entry.CqZone : Dxcc?.CqZone ?? 0;
    public int ItuZone => Entry.ItuZone > 0 ? Entry.ItuZone : Dxcc?.ItuZone ?? 0;
}

/// <summary>Come si conta l'avanzamento.</summary>
public enum AwardScore
{
    /// <summary>Numero di voci diverse (paesi, continenti, stazioni, prefissi…).</summary>
    Distinct,
    /// <summary>Punti banda: una voce vale un punto per ogni banda diversa (al massimo MaxBandsPerKey).</summary>
    BandPoints,
    /// <summary>Le migliori BandCount bande, ognuna con almeno PerBand voci (es. DXCC 5 bande).</summary>
    BestBands,
}

/// <summary>Classe di un diploma: si raggiunge con Count voci (e, se indicati, Points punti banda).</summary>
public sealed record AwardLevel(string Label, int Count, int Points = 0);

/// <summary>Una voce di un diploma a elenco chiuso (es. "Italy", oppure "Great Britain" = G+GM+GW+GI).</summary>
public sealed record AwardItem(string Key, string Label, string[] Entities);

public sealed record AwardDef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Sponsor { get; init; }
    public required string Group { get; init; }
    public string Url { get; init; } = "";
    /// <summary>Regole in breve, in italiano.</summary>
    public required string Rules { get; init; }

    /// <summary>Voce del diploma per il QSO, o null se il QSO non conta.</summary>
    public required Func<AwardQso, string?> Key { get; init; }
    /// <summary>Più voci per lo stesso QSO (se indicato, sostituisce Key).</summary>
    public Func<AwardQso, IEnumerable<string>>? Keys { get; init; }
    /// <summary>Elenco completo delle voci (per mostrare quelle mancanti); null se aperto (stazioni, prefissi).</summary>
    public IReadOnlyList<AwardItem>? Items { get; init; }
    /// <summary>Le voci vanno mostrate sulla mappa (sono entità DXCC).</summary>
    public bool Map { get; init; }

    public AwardScore Score { get; init; } = AwardScore.Distinct;
    /// <summary>Bande valide (null = tutte).</summary>
    public string[]? Bands { get; init; }
    public int MaxBandsPerKey { get; init; } = int.MaxValue;
    public int BandCount { get; init; }
    public int PerBand { get; init; }
    /// <summary>Una voce conta solo con almeno N stazioni diverse / N bande diverse.</summary>
    public int CallsPerKey { get; init; } = 1;
    public int BandsPerKey { get; init; } = 1;
    /// <summary>Voci obbligatorie per qualunque classe.</summary>
    public string[] Required { get; init; } = Array.Empty<string>();
    /// <summary>QSO validi da questa data (inclusa).</summary>
    public DateTime? Since { get; init; }
    public required AwardLevel[] Levels { get; init; }
    /// <summary>Requisito aggiuntivo calcolato a parte (es. la parola da comporre con le ultime lettere).</summary>
    public Func<IReadOnlyList<AwardQso>, ExtraCheck>? Extra { get; init; }
}

public sealed record ExtraCheck(bool Done, string Text);

/// <summary>Una voce del diploma nel log: stazioni e bande con cui è stata collegata.</summary>
public sealed record AwardItemProgress(string Key, string Label, bool Counts, IReadOnlyList<string> Bands,
    IReadOnlyList<string> Calls, int Qsos, DxccEntity? Entity);

public sealed record AwardBandProgress(string Band, int Count, int Target);

public sealed record AwardResult(
    AwardDef Def, int Progress, int Points, AwardLevel? Reached, AwardLevel? Next,
    IReadOnlyList<AwardItemProgress> Items, IReadOnlyList<AwardBandProgress> BandRows,
    IReadOnlyList<string> MissingRequired, ExtraCheck? Extra)
{
    /// <summary>Valore da confrontare con la prossima classe (i punti banda se la classe conta solo quelli).</summary>
    public int Value => (Next ?? Reached) is { Count: 0, Points: > 0 } ? Points : Progress;
    public int Target => (Next ?? Reached) is { } l ? (l.Count == 0 ? l.Points : l.Count) : 0;
}

public static class AwardEngine
{
    public static AwardResult Evaluate(AwardDef def, IReadOnlyList<AwardQso> qsos, Func<string, DxccEntity?>? entityByName = null)
    {
        var valid = qsos.Where(q => (def.Since is null || q.Entry.Date is null || q.Entry.Date >= def.Since)
                                    && (def.Bands is null || def.Bands.Contains(q.Band))).ToList();

        var byKey = new Dictionary<string, (HashSet<string> Calls, HashSet<string> Bands, int Qsos, DxccEntity? Ent)>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in valid)
        {
            var keys = def.Keys?.Invoke(q) ?? (def.Key(q) is { } one ? new[] { one } : Array.Empty<string>());
            foreach (var k in keys.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!byKey.TryGetValue(k, out var v)) byKey[k] = v = (new(StringComparer.OrdinalIgnoreCase), new(), 0, q.Wae ?? q.Dxcc);
                v.Calls.Add(Callsign.Base(q.Call));
                v.Bands.Add(q.Band);
                byKey[k] = v with { Qsos = v.Qsos + 1 };
            }
        }

        bool Counts(string k) => byKey.TryGetValue(k, out var v) && v.Calls.Count >= def.CallsPerKey && v.Bands.Count >= def.BandsPerKey;
        var counting = byKey.Keys.Where(Counts).ToList();

        int progress, points = 0;
        var bandRows = new List<AwardBandProgress>();
        switch (def.Score)
        {
            case AwardScore.BandPoints:
                progress = counting.Count;
                points = counting.Sum(k => Math.Min(byKey[k].Bands.Count, def.MaxBandsPerKey));
                break;
            case AwardScore.BestBands:
                var bands = def.Bands ?? BandPlan.AllBands;
                var perBand = bands.Select(b => new AwardBandProgress(b,
                    counting.Count(k => byKey[k].Bands.Contains(b)), def.PerBand)).ToList();
                bandRows = perBand;
                progress = perBand.Select(r => Math.Min(r.Count, def.PerBand)).OrderByDescending(n => n).Take(def.BandCount).Sum();
                break;
            default:
                progress = counting.Count;
                break;
        }

        var missingRequired = def.Required.Where(r => !Counts(r)).ToList();
        var extra = def.Extra?.Invoke(valid);
        bool ok = missingRequired.Count == 0 && (extra?.Done ?? true);
        var reached = ok ? def.Levels.LastOrDefault(l => progress >= l.Count && points >= l.Points) : null;
        var next = def.Levels.FirstOrDefault(l => progress < l.Count || points < l.Points || !ok);

        IEnumerable<AwardItemProgress> items;
        AwardItemProgress Row(string key, string label, DxccEntity? ent) =>
            byKey.TryGetValue(key, out var v)
                ? new(key, label, Counts(key), v.Bands.OrderBy(BandIndex).ToList(), v.Calls.OrderBy(c => c).ToList(), v.Qsos, v.Ent ?? ent)
                : new(key, label, false, Array.Empty<string>(), Array.Empty<string>(), 0, ent);
        if (def.Items is not null)
            items = def.Items.Select(i => Row(i.Key, i.Label, i.Entities.Length > 0 ? entityByName?.Invoke(i.Entities[0]) : null));
        else
            items = byKey.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Select(k => Row(k, k, null));

        return new AwardResult(def, progress, points, reached, next, items.ToList(), bandRows, missingRequired, extra);
    }

    public static int BandIndex(string band) => Array.IndexOf(BandPlan.AllBands, band) is var i and >= 0 ? i : 99;
}

/// <summary>Regole sui nominativi usate dai diplomi.</summary>
public static class Callsign
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase)
        { "P", "M", "MM", "AM", "QRP", "QRPP", "A", "B", "LH", "LGT", "J", "Y", "R", "T" };

    private static bool LooksLikeCall(string s) => s.Length >= 3 && s.Any(char.IsDigit) && s.Any(char.IsLetter) && char.IsLetter(s[^1]);

    /// <summary>Nominativo dell'operatore senza prefissi o suffissi di località: "IS0/IK3GER/P" → "IK3GER".</summary>
    public static string Base(string call)
    {
        var parts = call.ToUpperInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries);
        var calls = parts.Where(LooksLikeCall).ToList();
        if (calls.Count == 0) return parts.FirstOrDefault() ?? call;
        return calls.OrderByDescending(p => p.Length).First();
    }

    private static readonly Regex PrefixRx = new(@"^([A-Z0-9]*?\d+)[A-Z]+$", RegexOptions.Compiled);

    /// <summary>Prefisso secondo le regole WPX: "IK3GER" → "IK3", "3DA0X" → "3DA0", "W1AW/4" → "W4",
    /// "CT1AAA/CT3" → "CT3", "DL1AB/OZ" → "OZ0", "RAEM" → "RA0".</summary>
    public static string WpxPrefix(string call)
    {
        var parts = call.ToUpperInvariant().Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Modifiers.Contains(p)).ToList();
        if (parts.Count == 0) return "";
        var main = Base(string.Join("/", parts));
        var others = parts.Where(p => p != main).ToList();
        if (others.Count > 0)
        {
            var loc = others.OrderBy(p => p.Length).First();
            if (loc.Length == 1 && char.IsDigit(loc[0]))
            {
                var own = OwnPrefix(main);
                var letters = own.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                return letters + loc;
            }
            return loc.Any(char.IsDigit) ? OwnPrefix(loc + (char.IsLetter(loc[^1]) ? "" : "X")) : loc + "0";
        }
        return OwnPrefix(main);
    }

    private static string OwnPrefix(string call)
    {
        var m = PrefixRx.Match(call);
        if (m.Success) return m.Groups[1].Value;
        if (call.Any(char.IsDigit))
        {
            // es. "CT3" o una località già ridotta al solo prefisso
            var last = call.LastIndexOfAny("0123456789".ToCharArray());
            return call[..(last + 1)];
        }
        return (call.Length >= 2 ? call[..2] : call) + "0";
    }
}
