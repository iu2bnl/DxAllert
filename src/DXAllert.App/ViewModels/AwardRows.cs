using DXAllert.Core;

namespace DXAllert.App.ViewModels;

/// <summary>Un diploma nella tabella della scheda Diplomi, con i dettagli per il pannello a destra.</summary>
public sealed class AwardRow
{
    public AwardRow(AwardResult r)
    {
        Result = r;
        var d = r.Def;
        var target = r.Target;
        Percent = target > 0 ? Math.Min(100, 100.0 * r.Value / target) : 0;
        ProgressText = target > 0 ? $"{r.Value} / {target}" : r.Value.ToString();
        if (d.Score == AwardScore.BandPoints && d.Levels.Any(l => l.Count > 0 && l.Points > 0))
            ProgressText = $"{r.Progress} paesi · {r.Points} punti";
        ReachedText = r.Reached?.Label ?? "—";
        NextText = r.Next is { } n
            ? (r.MissingRequired.Count > 0 ? $"{n.Label} (manca {string.Join(", ", r.MissingRequired)})"
               : r.Extra is { Done: false } ? $"{n.Label} (vedi requisiti)"
               : n.Label)
            : "Completato";
        IsDone = r.Next is null;

        Levels = d.Levels.Select(l => new AwardLevelRow(l.Label,
            Describe(l, d), r.Reached is { } reached && Array.IndexOf(d.Levels, l) <= Array.IndexOf(d.Levels, reached))).ToList();
        Bands = r.BandRows.Select(b => new AwardBandRow(b.Band, b.Count, b.Target)).ToList();

        // Voci: per gli elenchi chiusi anche le mancanti; per stazioni e prefissi solo quelle collegate.
        Items = r.Items.Select(i => new AwardItemRow(i)).ToList();
        var worked = r.Items.Count(i => i.Qsos > 0);
        ItemsTitle = d.Items is null
            ? $"Collegati: {worked}"
            : $"Elenco: {r.Items.Count(i => i.Counts)} validi, {worked - r.Items.Count(i => i.Counts)} collegati ma non ancora validi, {r.Items.Count - worked} da collegare";
        var notes = new List<string>();
        if (r.MissingRequired.Count > 0) notes.Add("Obbligatori mancanti: " + string.Join(", ", r.MissingRequired) + ".");
        if (r.Extra is { } x) notes.Add(x.Text);
        if (d.Since is { } s) notes.Add("Contano solo i QSO dal " + s.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("it-IT")) + ".");
        if (d.Bands is { } b && d.Score != AwardScore.BestBands) notes.Add("Bande valide: " + string.Join(" ", b) + ".");
        if (d.CallsPerKey > 1) notes.Add($"Ogni paese vale con almeno {d.CallsPerKey} stazioni diverse.");
        if (d.BandsPerKey > 1) notes.Add($"Ogni voce vale con almeno {d.BandsPerKey} bande diverse.");
        Notes = string.Join(" ", notes);
    }

    private static string Describe(AwardLevel l, AwardDef d) =>
        l.Label == l.Count.ToString() || l.Label == l.Points.ToString() ? ""
        : l.Count > 0 && l.Points > 0 ? $"{l.Count} paesi e {l.Points} punti"
        : l.Count == 0 ? $"{l.Points} punti banda"
        : d.Score == AwardScore.BestBands ? $"{l.Count} su {d.BandCount} bande"
        : l.Count.ToString();

    public AwardResult Result { get; }
    public string Id => Result.Def.Id;
    public string Name => Result.Def.Name;
    public string Sponsor => Result.Def.Sponsor;
    public string Group => Result.Def.Group;
    public string Rules => Result.Def.Rules;
    public string Url => Result.Def.Url;
    public bool HasUrl => Url.Length > 0;
    public bool HasMap => Result.Def.Map;
    public double Percent { get; }
    public double Fraction => Percent / 100;
    public string ProgressText { get; }
    public string ReachedText { get; }
    public string NextText { get; }
    public bool IsDone { get; }
    public IReadOnlyList<AwardLevelRow> Levels { get; }
    public IReadOnlyList<AwardBandRow> Bands { get; }
    public bool HasBands => Bands.Count > 0;
    public IReadOnlyList<AwardItemRow> Items { get; }
    public string ItemsTitle { get; }
    public string Notes { get; }
    public bool HasNotes => Notes.Length > 0;
}

public sealed record AwardLevelRow(string Label, string Requirement, bool Reached)
{
    public string Mark => Reached ? "✓" : "○";
    public string Color => Reached ? "#1FA055" : "#8D99A6";
}

public sealed record AwardBandRow(string Band, int Count, int Target)
{
    public string Text => $"{Count} / {Target}";
    public double Percent => Target > 0 ? Math.Min(100, 100.0 * Count / Target) : 0;
    public double Fraction => Percent / 100;
}

public sealed class AwardItemRow
{
    public AwardItemRow(AwardItemProgress p)
    {
        Label = p.Label;
        State = p.Counts ? "done" : p.Qsos > 0 ? "partial" : "missing";
        Color = State switch { "done" => "#1FA055", "partial" => "#FFA726", _ => "#8D99A6" };
        Tooltip = p.Qsos == 0
            ? $"{p.Label}: da collegare"
            : $"{p.Label}: {p.Qsos} QSO · bande {string.Join(" ", p.Bands)}" +
              (p.Calls.Count > 0 ? $" · {string.Join(", ", p.Calls.Take(8))}{(p.Calls.Count > 8 ? $" e altri {p.Calls.Count - 8}" : "")}" : "");
        Entity = p.Entity;
    }

    public string Label { get; }
    /// <summary>"done", "partial" o "missing".</summary>
    public string State { get; }
    public string Color { get; }
    public string Tooltip { get; }
    public DxccEntity? Entity { get; }
    public bool IsMissing => State == "missing";
}
