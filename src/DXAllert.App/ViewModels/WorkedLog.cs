using DXAllert.Core;

namespace DXAllert.App.ViewModels;

/// <summary>Log ADIF importato: lookup in memoria per callsign e entità DXCC.</summary>
public sealed class WorkedLog
{
    private readonly Dictionary<string, List<string>> _byCall;
    private readonly Dictionary<string, List<string>> _byEntity;

    public WorkedLog(IEnumerable<WorkedEntry> entries)
    {
        _byCall = new(StringComparer.OrdinalIgnoreCase);
        _byEntity = new(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (!_byCall.TryGetValue(e.Callsign, out var cl)) _byCall[e.Callsign] = cl = new();
            var bm = $"{e.Band} {e.Mode}";
            if (!cl.Contains(bm, StringComparer.OrdinalIgnoreCase)) cl.Add(bm);
            if (e.DxccName.Length > 0)
            {
                if (!_byEntity.TryGetValue(e.DxccName, out var el)) _byEntity[e.DxccName] = el = new();
                if (!el.Contains(bm, StringComparer.OrdinalIgnoreCase)) el.Add(bm);
            }
            Count++;
        }
    }

    public int Count { get; }
    public int UniqueCallsigns => _byCall.Count;
    public int UniqueEntities => _byEntity.Count;

    /// <summary>
    /// "✓ 20m FT8 · 15m CW" se il callsign è nel log;
    /// "~ 20m SSB" se solo l'entità DXCC è nel log;
    /// "" se non trovato.
    /// </summary>
    public string SummaryFor(string callsign, string entityName)
    {
        if (_byCall.TryGetValue(callsign, out var modes) && modes.Count > 0)
            return "✓ " + string.Join(" · ", modes.Take(5));
        var entity = entityName.Trim();
        if (entity.Length > 0 && _byEntity.TryGetValue(entity, out var em) && em.Count > 0)
            return "~ " + string.Join(" · ", em.Take(5));
        return "";
    }
}
