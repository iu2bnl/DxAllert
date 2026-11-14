using System.Text.RegularExpressions;
using System.Xml.Linq;
using DXAllert.Core;

namespace DXAllert.Data.Sources;

/// <summary>
/// DX-World: feed RSS delle novità. Non contiene date strutturate: le voci servono a
/// integrare (link, fonte) quelle delle altre fonti e a segnalare annunci nuovi.
/// </summary>
public sealed class DxWorldSource : IDxpeditionSource
{
    public const string FeedUrl = "https://www.dx-world.net/feed/";
    public string Name => "DX-World";

    public async Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct) =>
        Parse(await http.GetStringAsync(FeedUrl, ct));

    public static IReadOnlyList<Dxpedition> Parse(string xml)
    {
        var list = new List<Dxpedition>();
        foreach (var item in XDocument.Parse(xml).Descendants("item"))
        {
            // "PJ6/DL8DYL & PJ6/DL9DRA – Saba"
            var title = ((string?)item.Element("title") ?? "").Trim();
            var dash = title.IndexOfAny(new[] { '–', '—' });
            if (dash < 0) continue;
            var entity = Regex.Replace(title[(dash + 1)..], @"\bDXpedition\b.*$", "", RegexOptions.IgnoreCase).Trim();
            var link = ((string?)item.Element("link") ?? "").Trim();
            foreach (var raw in Regex.Split(title[..dash], @",|&|\band\b"))
            {
                var call = raw.Trim().ToUpperInvariant();
                if (!CallsignUtil.IsFullCallsign(call)) continue;
                list.Add(new Dxpedition { Callsign = call, EntityName = entity, Url = link, Sources = { "DX-World" } });
            }
        }
        return list;
    }
}

/// <summary>
/// DXNews: predisposto ma disattivato. Alla verifica dell'11/11/2026 il dominio dxnews.com
/// non risultava nel DNS. Per riattivarlo: impostare l'indirizzo del feed e aggiungerlo in DxpeditionService.
/// </summary>
public sealed class DxNewsSource : IDxpeditionSource
{
    public string Name => "DXNews";
    public string? FeedUrl { get; init; }

    public async Task<IReadOnlyList<Dxpedition>> FetchAsync(HttpClient http, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(FeedUrl)) return Array.Empty<Dxpedition>();
        // Stesso formato "NOMINATIVO – Entità" di un feed RSS di notizie.
        return DxWorldSource.Parse(await http.GetStringAsync(FeedUrl, ct))
            .Select(d => { d.Sources = new List<string> { Name }; return d; }).ToList();
    }
}
