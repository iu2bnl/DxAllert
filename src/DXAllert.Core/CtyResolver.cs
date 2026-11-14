using System.Globalization;
using System.Text.RegularExpressions;

namespace DXAllert.Core;

/// <summary>
/// Riconoscimento nominativo -> entità DXCC dal file cty.dat di country-files.com.
/// </summary>
public sealed class CtyResolver
{
    private readonly Dictionary<string, DxccEntity> _prefixes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DxccEntity> _exact = new(StringComparer.Ordinal);
    private readonly List<DxccEntity> _entities = new();
    private int _maxPrefixLen = 1;

    private static readonly Regex Overrides = new(@"\(\d+\)|\[\d+\]|<[^>]*>|\{[^}]*\}|~[^~]*~", RegexOptions.Compiled);
    private static readonly Regex CqOverride = new(@"\((\d+)\)", RegexOptions.Compiled);
    private static readonly Regex ItuOverride = new(@"\[(\d+)\]", RegexOptions.Compiled);

    // Suffissi dopo la barra che non cambiano l'entità.
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
        { "P", "M", "QRP", "QRPP", "A", "B", "LH", "LGT", "J", "Y", "R", "T" };

    public IReadOnlyList<DxccEntity> Entities => _entities;
    public string Version { get; private set; } = "";

    public static CtyResolver Parse(string ctyDat)
    {
        var r = new CtyResolver();
        // Ogni entità: riga di testata con 8 campi separati da ':' poi elenco alias chiuso da ';'.
        foreach (var block in ctyDat.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = block.Trim();
            if (text.Length == 0) continue;
            var f = text.Split(':');
            if (f.Length < 9) continue;
            var primary = f[7].Trim();
            // Con '*' sono entità solo WAEDC (es. IT9, Shetland): non sono DXCC, si ricade sul paese madre.
            if (primary.StartsWith('*')) continue;
            if (!int.TryParse(f[1].Trim(), out var cq) || !int.TryParse(f[2].Trim(), out var itu)) continue;
            var lat = double.Parse(f[4].Trim(), CultureInfo.InvariantCulture);
            // In cty.dat la longitudine è positiva verso OVEST.
            var lon = -double.Parse(f[5].Trim(), CultureInfo.InvariantCulture);
            var entity = new DxccEntity(f[0].Trim(), cq, itu, f[3].Trim(), lat, lon, primary);
            r._entities.Add(entity);

            foreach (var raw in string.Join(":", f.Skip(8)).Split(','))
            {
                var alias = raw.Trim();
                if (alias.Length == 0) continue;
                var e = entity;
                var mc = CqOverride.Match(alias);
                var mi = ItuOverride.Match(alias);
                if (mc.Success || mi.Success)
                    e = entity with
                    {
                        CqZone = mc.Success ? int.Parse(mc.Groups[1].Value) : entity.CqZone,
                        ItuZone = mi.Success ? int.Parse(mi.Groups[1].Value) : entity.ItuZone,
                    };
                alias = Overrides.Replace(alias, "").ToUpperInvariant();
                if (alias.StartsWith('='))
                {
                    var call = alias[1..];
                    if (call.StartsWith("VER") && call.Length == 11) { r.Version = call[3..]; continue; }
                    r._exact[call] = e;
                }
                else
                {
                    r._prefixes[alias] = e;
                    if (alias.Length > r._maxPrefixLen) r._maxPrefixLen = alias.Length;
                }
            }
        }
        return r;
    }

    /// <summary>
    /// Come <see cref="Resolve(string?)"/>, ma se la fonte indica un nome di entità che coincide con
    /// una del cty.dat vince quello: i nominativi nuovi delle DXpedition (3Y0L, VP0SG…) spesso
    /// non sono ancora nel file e il solo prefisso porterebbe all'entità sbagliata.
    /// </summary>
    public DxccEntity? Resolve(string? callsign, string? entityHint)
    {
        var key = NameKey(entityHint);
        if (key.Length > 0)
        {
            _byName ??= _entities.GroupBy(e => NameKey(e.Name)).Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.First());
            if (_byName.TryGetValue(key, out var named)) return named;
        }
        return Resolve(callsign);
    }

    private Dictionary<string, DxccEntity>? _byName;
    private static readonly HashSet<string> NameNoise = new() { "island", "islands", "is", "i", "1", "and", "the", "of" };

    // "Peter I", "Peter 1 Island" -> "peter"; "Wallis & Futuna Islands" -> "wallis futuna"
    private static string NameKey(string? name) =>
        string.Join(" ", Regex.Split((name ?? "").ToLowerInvariant(), @"[^a-z0-9]+")
            .Where(t => t.Length > 0 && !NameNoise.Contains(t)));

    public DxccEntity? Resolve(string? callsign)
    {
        if (string.IsNullOrWhiteSpace(callsign)) return null;
        var call = callsign.Trim().ToUpperInvariant();
        if (_exact.TryGetValue(call, out var hit)) return hit;

        var parts = call.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0) return null;
        // /MM e /AM: fuori da ogni entità DXCC.
        if (parts.Skip(1).Any(p => p is "MM" or "AM")) return null;

        if (parts.Count > 1)
        {
            // Togli i modificatori (/P, /QRP, lettera singola, cifra singola) ma non il primo elemento.
            var kept = new List<string> { parts[0] };
            kept.AddRange(parts.Skip(1).Where(p => !Modifiers.Contains(p) && p.Length > 1));
            parts = kept;
            if (parts.Count > 1 && _exact.TryGetValue(string.Join("/", parts), out hit)) return hit;
        }

        if (parts.Count == 1) return MatchPrefix(parts[0]);

        // "3B9/M0CFW" oppure "M0CFW/3B9": il prefisso di destinazione è la parte più corta
        // (a parità, la prima).
        var a = parts[0];
        var b = parts[1];
        var prefix = b.Length < a.Length ? b : a;
        return MatchPrefix(prefix) ?? MatchPrefix(prefix == a ? b : a);
    }

    private DxccEntity? MatchPrefix(string s)
    {
        if (_exact.TryGetValue(s, out var hit)) return hit;
        for (int len = Math.Min(s.Length, _maxPrefixLen); len >= 1; len--)
            if (_prefixes.TryGetValue(s[..len], out hit)) return hit;
        return null;
    }
}
