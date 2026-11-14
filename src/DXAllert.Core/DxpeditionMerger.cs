using System.Text.RegularExpressions;

namespace DXAllert.Core;

public static class CallsignUtil
{
    private static readonly Regex Full = new(@"^(?:[A-Z0-9]{1,4}/)?[A-Z0-9]{1,3}[0-9][A-Z0-9]*[A-Z](?:/[A-Z0-9]{1,4})?$", RegexOptions.Compiled);

    /// <summary>True se è un nominativo completo e non un semplice prefisso ("3B9").</summary>
    public static bool IsFullCallsign(string? s) =>
        !string.IsNullOrWhiteSpace(s) && Full.IsMatch(s.Trim().ToUpperInvariant());

    /// <summary>Forma di confronto: maiuscolo, senza /P /M /QRP finali.</summary>
    public static string Normalize(string? call)
    {
        var c = (call ?? "").Trim().ToUpperInvariant();
        return Regex.Replace(c, @"/(P|M|QRP|A)$", "");
    }
}

/// <summary>
/// Unisce gli elenchi delle fonti: stessa stazione = stesso nominativo e date sovrapposte.
/// L'ordine delle liste in ingresso è l'ordine di priorità dei campi.
/// </summary>
public static class DxpeditionMerger
{
    public static List<Dxpedition> Merge(IEnumerable<IEnumerable<Dxpedition>> listsByPriority)
    {
        var result = new List<Dxpedition>();
        var byCall = new Dictionary<string, List<Dxpedition>>(StringComparer.Ordinal);

        foreach (var list in listsByPriority)
        foreach (var d in list)
        {
            var key = CallsignUtil.Normalize(d.Callsign);
            if (key.Length == 0) continue;
            if (!byCall.TryGetValue(key, out var same)) byCall[key] = same = new List<Dxpedition>();

            var match = same.FirstOrDefault(x => Overlaps(x, d));
            if (match is null)
            {
                var copy = Clone(d);
                same.Add(copy);
                result.Add(copy);
            }
            else Absorb(match, d);
        }
        return result
            .OrderBy(d => d.StartUtc ?? d.EndUtc ?? DateTime.MaxValue)
            .ThenBy(d => d.Callsign, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Date sovrapposte; una data mancante vale come intervallo aperto.</summary>
    public static bool Overlaps(Dxpedition a, Dxpedition b)
    {
        var aStart = a.StartUtc ?? DateTime.MinValue;
        var aEnd = a.EndUtc ?? DateTime.MaxValue;
        var bStart = b.StartUtc ?? DateTime.MinValue;
        var bEnd = b.EndUtc ?? DateTime.MaxValue;
        return aStart.Date <= bEnd.Date && bStart.Date <= aEnd.Date;
    }

    private static void Absorb(Dxpedition into, Dxpedition from)
    {
        into.StartUtc ??= from.StartUtc;
        into.EndUtc ??= from.EndUtc;
        if (into.EntityName.Length == 0) into.EntityName = from.EntityName;
        if (into.Bands.Length == 0) into.Bands = from.Bands;
        if (into.Modes.Length == 0) into.Modes = from.Modes;
        if (into.QslVia.Length == 0) into.QslVia = from.QslVia;
        if (into.Url.Length == 0) into.Url = from.Url;
        if (into.Iota.Length == 0) into.Iota = from.Iota;
        if (into.Info.Length == 0) into.Info = from.Info;
        into.IsSpecialEvent &= from.IsSpecialEvent;
        foreach (var s in from.Sources)
            if (!into.Sources.Contains(s)) into.Sources.Add(s);
    }

    private static Dxpedition Clone(Dxpedition d) => new()
    {
        Callsign = d.Callsign.Trim().ToUpperInvariant(), EntityName = d.EntityName, StartUtc = d.StartUtc,
        EndUtc = d.EndUtc, Bands = d.Bands, Modes = d.Modes, QslVia = d.QslVia, Url = d.Url, Iota = d.Iota,
        Info = d.Info, IsSpecialEvent = d.IsSpecialEvent, Sources = new List<string>(d.Sources), Entity = d.Entity,
    };
}
