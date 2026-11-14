using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DXAllert.Core;

namespace DXAllert.Data.Sources;

/// <summary>
/// DXZone: legge i dati strutturati schema.org (JSON-LD) delle pagine consentite dal robots.txt.
/// Le viste con parametri (?ym=, ?view=) sono vietate dal robots.txt e non vengono usate.
/// </summary>
public sealed class DxZoneSource : IDxpeditionSource
{
    public static readonly string[] Pages =
    {
        "https://www.dxzone.com/events/dxpeditions/",
        "https://www.dxzone.com/events/dxpeditions/today/",
        "https://www.dxzone.com/events/dxpeditions/this-weekend/",
    };
    public string Name => "DXZone";

    private static readonly Regex LdJson = new(
        @"<script[^>]*type=['""]application/ld\+json['""][^>]*>(.*?)</script>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        var all = new List<Dxpedition>();
        Exception? last = null;
        foreach (var url in Pages)
        {
            try
            {
                all.AddRange(Parse(await http.GetStringAsync(url, ct)));
                await Task.Delay(1500, ct); // richieste distanziate
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { last = ex; }
        }
        if (all.Count == 0 && last is not null) throw last;
        return DxpeditionMerger.Merge(new[] { all });
    }

    public static IReadOnlyList<Dxpedition> Parse(string html)
    {
        var list = new List<Dxpedition>();
        foreach (Match m in LdJson.Matches(html))
        {
            try
            {
                using var doc = JsonDocument.Parse(m.Groups[1].Value);
                Collect(doc.RootElement, list);
            }
            catch (JsonException) { /* blocco non valido: si ignora */ }
        }
        return list;
    }

    private static void Collect(JsonElement el, List<Dxpedition> list)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var x in el.EnumerateArray()) Collect(x, list);
                break;
            case JsonValueKind.Object:
                if (el.TryGetProperty("@type", out var t) && t.ValueKind == JsonValueKind.String &&
                    t.GetString() == "Event")
                {
                    var d = FromEvent(el);
                    if (d is not null) list.Add(d);
                    break;
                }
                foreach (var p in el.EnumerateObject()) Collect(p.Value, list);
                break;
        }
    }

    private static Dxpedition? FromEvent(JsonElement ev)
    {
        // "name":"KH8A American Samoa"
        var name = Str(ev, "name").Trim();
        var sp = name.IndexOf(' ');
        var call = (sp < 0 ? name : name[..sp]).ToUpperInvariant();
        if (!Regex.IsMatch(call, @"^[A-Z0-9/]{3,}$") || !call.Any(char.IsDigit)) return null;
        var start = Date(Str(ev, "startDate"));
        var end = Date(Str(ev, "endDate"));
        if (start is null && end is null) return null;
        return new Dxpedition
        {
            Callsign = call, EntityName = sp < 0 ? "" : name[(sp + 1)..].Trim(),
            StartUtc = start, EndUtc = end, Url = Str(ev, "url"), Sources = { "DXZone" },
        };
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static DateTime? Date(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? DateTime.SpecifyKind(d.UtcDateTime.Date, DateTimeKind.Utc)
            : null;
}
