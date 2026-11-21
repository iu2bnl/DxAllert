using System.IO;
using DXAllert.Core;

namespace DXAllert.App.Services;

/// <summary>
/// Numero DXCC (ADIF) e bandiera di ciascuna entità, letti da Assets\dxcc.csv
/// (prefisso principale come in cty.dat;numero;codice bandiera). Le bandiere sono in Assets\flags.
/// </summary>
public static class DxccInfo
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Assets");
    private static readonly Lazy<Dictionary<string, (int Adif, string? Flag)>> Table = new(Load);

    private static Dictionary<string, (int, string?)> Load()
    {
        var map = new Dictionary<string, (int, string?)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadLines(Path.Combine(Dir, "dxcc.csv")))
            {
                var f = line.Split(';');
                if (f.Length < 3 || !int.TryParse(f[1], out var adif)) continue;
                var flag = f[2].Length > 0 ? Path.Combine(Dir, "flags", f[2] + ".png") : null;
                map[f[0].TrimStart('*')] = (adif, flag is not null && File.Exists(flag) ? flag : null);
            }
        }
        catch (Exception ex) { Log.Warn($"dxcc.csv non leggibile: {ex.Message}"); }
        return map;
    }

    private static (int Adif, string? Flag)? Find(DxccEntity? e) =>
        e is not null && Table.Value.TryGetValue(e.PrimaryPrefix.TrimStart('*'), out var v) ? v : null;

    /// <summary>Percorso della bandiera, o null se non c'è.</summary>
    public static string? Flag(DxccEntity? e) => Find(e)?.Flag;

    /// <summary>"EU 248": continente e numero DXCC.</summary>
    public static string Code(DxccEntity? e) =>
        e is null ? "" : Find(e) is { } v ? $"{e.Continent} {v.Adif}" : e.Continent;
}
