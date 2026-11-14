using DXAllert.Core;
using DXAllert.Data;
using DXAllert.Data.Sources;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Test dei parser sui campioni reali scaricati l'11/11/2026 (cartella Samples).</summary>
public class ParserTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 19, 20, 0, DateTimeKind.Utc);

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static DateTime D(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Ng3k_ParsesFeed()
    {
        var list = Ng3kSource.Parse(Sample("ng3k_adxo.xml"));
        Assert.True(list.Count > 50);

        var h44 = list.First(d => d.Callsign == "H44MS");
        Assert.Equal("Solomon Is", h44.EntityName);
        Assert.Equal(D(2026, 10, 3), h44.StartUtc);
        Assert.Equal(D(2026, 11, 20), h44.EndUtc);
        Assert.Equal("LoTW", h44.QslVia);
        Assert.Equal("80-10m", h44.Bands);
        Assert.Contains("FT8", h44.Modes);
        Assert.Contains("NG3K", h44.Sources);

        // Operazione elencata col solo prefisso "3B9": il nominativo vero è nel testo.
        Assert.Contains(list, d => d.Callsign == "3B9/M0CFW" && d.EntityName == "Rodrigues I");
        Assert.Contains(list, d => d.Callsign == "3B8/E70A" && d.EntityName == "Mauritius");
        Assert.Contains(list, d => d.Callsign == "ZD7WP");
        Assert.Contains(list, d => d.Callsign == "ZD7VJ");
        Assert.Contains(list, d => d.Callsign == "K6VHF/HR9");
        Assert.Contains(list, d => d.Callsign == "FS/DL1MGB");
        Assert.Contains(list, d => d.Callsign == "T88XM");
        Assert.DoesNotContain(list, d => d.Callsign is "E70A" or "3B9" or "ZD7");
        // A cavallo d'anno.
        var vp8 = list.First(d => d.Callsign == "VP8THW");
        Assert.Equal(D(2026, 12, 12), vp8.StartUtc);
        Assert.Equal(D(2027, 1, 2), vp8.EndUtc);
    }

    [Theory]
    [InlineData("Oct 3-Nov 20, 2026", "2026-10-03", "2026-11-20")]
    [InlineData("Nov 3-16, 2026", "2026-11-03", "2026-11-16")]
    [InlineData("Dec 12, 2026-Jan 2, 2027", "2026-12-12", "2027-01-02")]
    [InlineData("Dec 28-Jan 5, 2027", "2026-12-28", "2027-01-05")]
    [InlineData("Nov 15, 2026", "2026-11-15", "2026-11-15")]
    public void Ng3k_ParsesDateFormats(string text, string start, string end)
    {
        Assert.True(Ng3kSource.TryParseDates(text, out var s, out var e));
        Assert.Equal(start, s.ToString("yyyy-MM-dd"));
        Assert.Equal(end, e.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void DxZone_ParsesJsonLd()
    {
        var list = DxZoneSource.Parse(Sample("dxzone_now.html"));
        Assert.Equal(13, list.Count);
        var kh8 = list.First(d => d.Callsign == "KH8A");
        Assert.Equal("American Samoa", kh8.EntityName);
        Assert.Equal(D(2026, 10, 30), kh8.StartUtc);
        Assert.Equal(D(2026, 11, 12), kh8.EndUtc);
        Assert.StartsWith("https://www.dxzone.com/events/dxpedition/", kh8.Url);

        Assert.Equal(8, DxZoneSource.Parse(Sample("dxzone_today.html")).Count);
        Assert.Empty(DxZoneSource.Parse("<html><body>nessun dato</body></html>"));
    }

    [Fact]
    public void Dx425_ParsesCalendar()
    {
        var list = Dx425Source.Parse(Sample("dx425_wcal.html"), Now);
        Assert.True(list.Count > 100);
        Assert.All(list, d => Assert.Contains("425 DX News", d.Sources));

        var kh8 = list.First(d => d.Callsign == "KH8A");
        Assert.Null(kh8.StartUtc);
        Assert.Equal(D(2026, 11, 12), kh8.EndUtc);
        Assert.Equal("American Samoa", kh8.EntityName);
        Assert.Equal("OC-045", kh8.Iota);
        Assert.False(kh8.IsSpecialEvent);

        // Più nominativi nella stessa riga.
        Assert.Contains(list, d => d.Callsign == "6W/I2YSB" && d.EntityName == "Senegal");
        Assert.Contains(list, d => d.Callsign == "6W/IK2HKT");
        var guinea = list.First(d => d.Callsign == "3X4U");
        Assert.Equal(D(2026, 11, 11), guinea.StartUtc);
        Assert.Equal(D(2026, 11, 23), guinea.EndUtc);

        Assert.True(list.First(d => d.Callsign == "CN1UNI").IsSpecialEvent);
        Assert.Equal(D(2027, 3, 26), list.First(d => d.Callsign == "VP0SG").EndUtc);
    }

    [Theory]
    [InlineData("till  08/11", null, "2026-11-08")]
    [InlineData("till  15/01 2027", null, "2027-01-15")]
    [InlineData("till  ??/01 2027", null, "2027-01-31")]
    [InlineData("till  December", null, "2026-12-31")]
    [InlineData("07/11-08/11", "2026-11-07", "2026-11-08")]
    [InlineData("15/11-28/02 2027", "2026-11-15", "2027-02-28")]
    [InlineData("05/02-04/05 2027", "2027-02-05", "2027-05-04")]
    [InlineData("November", "2026-11-01", "2026-11-30")]
    [InlineData("January     2027", "2027-01-01", "2027-01-31")]
    [InlineData("Jan-Mar     2027", "2027-01-01", "2027-03-31")]
    public void Dx425_ParsesPeriods(string text, string? start, string end)
    {
        Assert.True(Dx425Source.TryParsePeriod(text, Now, out var s, out var e));
        Assert.Equal(start, s?.ToString("yyyy-MM-dd"));
        Assert.Equal(end, e?.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void DxWorld_ParsesFeedTitles()
    {
        var list = DxWorldSource.Parse(Sample("dxworld_feed.xml"));
        Assert.Contains(list, d => d.Callsign == "PJ6/DL8DYL" && d.EntityName == "Saba");
        Assert.Contains(list, d => d.Callsign == "PJ6/DL9DRA");
        Assert.Contains(list, d => d.Callsign == "3X4U" && d.EntityName == "Guinea");
        // Le notizie generiche senza nominativo vengono scartate.
        Assert.DoesNotContain(list, d => d.Callsign.Contains("ON4UN"));
        Assert.All(list, d => Assert.StartsWith("https://www.dx-world.net/", d.Url));
    }

    [Fact]
    public void DxSummit_ParsesJsonAndSkipsOldIds()
    {
        long lastId = 0;
        var spots = DxSummitSpotSource.Parse(Sample("dxsummit.json"), "DXSummit", ref lastId);
        Assert.Equal(25, spots.Count);
        Assert.All(spots, s => Assert.True(s.FreqKhz > 0 && s.DxCall.Length > 0));
        Assert.True(lastId > 0);
        Assert.Empty(DxSummitSpotSource.Parse(Sample("dxsummit.json"), "DXSummit", ref lastId));
    }

    [Fact]
    public void SpotLine_ParsesClusterFormats()
    {
        var s = SpotLineParser.Parse("DX de IK2CLB:     7025.2  VK2ARZ       loud                           1919Z JN45", Now, "Cluster");
        Assert.NotNull(s);
        Assert.Equal("VK2ARZ", s!.DxCall);
        Assert.Equal(7025.2, s.FreqKhz);
        Assert.Equal("IK2CLB", s.Spotter);
        Assert.Equal("loud", s.Comment);
        Assert.Equal(new DateTime(2026, 11, 11, 19, 19, 0, DateTimeKind.Utc), s.TimeUtc);
        Assert.Equal("40m", s.Band);
        Assert.Equal("CW", s.Mode);

        var skimmer = SpotLineParser.Parse("DX de DF2CK-#:   14074.0  3X4U         FT8 -12 dB                     1855Z", Now, "Cluster");
        Assert.Equal("DF2CK", skimmer!.Spotter);
        Assert.Equal("FT8", skimmer.Mode);

        // Spot delle 23:58 ricevuto subito dopo mezzanotte: giorno precedente.
        var midnight = new DateTime(2026, 11, 12, 0, 1, 0, DateTimeKind.Utc);
        Assert.Equal(11, SpotLineParser.Parse("DX de K1ABC: 28490.0 KH8A up 5 2358Z", midnight, "x")!.TimeUtc.Day);

        Assert.Null(SpotLineParser.Parse("WWV de W0MU <18>:   SFI=140, A=8, K=2", Now, "x"));
        Assert.Null(SpotLineParser.Parse("To ALL de IZ0XXX: buongiorno", Now, "x"));
    }

    [Fact]
    public void Merger_JoinsSameCallWithOverlappingDates()
    {
        var zone = DxZoneSource.Parse(Sample("dxzone_now.html"));
        var d425 = Dx425Source.Parse(Sample("dx425_wcal.html"), Now);
        var ng3k = Ng3kSource.Parse(Sample("ng3k_adxo.xml"));
        var world = DxWorldSource.Parse(Sample("dxworld_feed.xml"));
        var merged = DxpeditionMerger.Merge(new[] { zone, d425, ng3k, world });

        // KH8A è in tutte e quattro le fonti: una sola voce.
        var kh8 = Assert.Single(merged, d => d.Callsign == "KH8A");
        Assert.Equal(new[] { "DXZone", "425 DX News", "NG3K", "DX-World" }, kh8.Sources);
        Assert.Equal(D(2026, 10, 30), kh8.StartUtc);
        Assert.Equal("Club Log OQRS", kh8.QslVia);
        Assert.Equal("OC-045", kh8.Iota);

        // H44MS ha due periodi distinti (autunno 2026 e 2027): restano due voci.
        Assert.Equal(2, merged.Count(d => d.Callsign == "H44MS"));
        Assert.Equal(merged.Count, merged.Select(d => (d.Callsign, d.StartUtc, d.EndUtc)).Distinct().Count());
    }

    [Fact]
    public void Merger_KeepsNonOverlappingSeparate()
    {
        var a = new Dxpedition { Callsign = "T88XX", StartUtc = D(2026, 1, 1), EndUtc = D(2026, 1, 10), Sources = { "A" } };
        var b = new Dxpedition { Callsign = "t88xx", StartUtc = D(2026, 1, 8), EndUtc = D(2026, 1, 12), Sources = { "B" }, QslVia = "JA1XX" };
        var c = new Dxpedition { Callsign = "T88XX", StartUtc = D(2026, 6, 1), EndUtc = D(2026, 6, 5), Sources = { "B" } };
        var merged = DxpeditionMerger.Merge(new[] { new[] { a }, new[] { b, c } });
        Assert.Equal(2, merged.Count);
        Assert.Equal(new[] { "A", "B" }, merged[0].Sources);
        Assert.Equal("JA1XX", merged[0].QslVia);
        Assert.Equal(D(2026, 1, 10), merged[0].EndUtc); // vince la fonte con priorità più alta
    }

    [Fact]
    public void AlertEngine_ThrottlesSameStationAndBand()
    {
        var engine = new AlertEngine();
        var settings = new AppSettings();
        var dx = new Dxpedition { Callsign = "3X4U", EntityName = "Guinea" };
        var spot = new Spot(Now, "3X4U", 14025.0, "IK2CLB", "", "Cluster");

        Assert.NotNull(engine.OnSpot(spot, dx, settings, Now));
        Assert.Null(engine.OnSpot(spot, dx, settings, Now.AddMinutes(9)));
        // Altra banda: avviso separato.
        Assert.NotNull(engine.OnSpot(spot with { FreqKhz = 21025.0 }, dx, settings, Now.AddMinutes(1)));
        Assert.NotNull(engine.OnSpot(spot, dx, settings, Now.AddMinutes(11)));

        // Banda o modo non di interesse: nessun avviso.
        settings.Bands.Remove("10m");
        Assert.Null(engine.OnSpot(spot with { FreqKhz = 28025.0 }, dx, settings, Now));
        settings.Modes.Remove("FT8");
        Assert.Null(engine.OnSpot(spot with { FreqKhz = 18100.5 }, dx, settings, Now));
    }

    [Fact]
    public void Repository_CachesSourcesAndSpots()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dxallert-test-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new Repository(path);
            Assert.Null(repo.LoadSourceCache("NG3K"));
            var items = Ng3kSource.Parse(Sample("ng3k_adxo.xml"));
            repo.SaveSourceCache("NG3K", items, Now);
            var cached = repo.LoadSourceCache("NG3K");
            Assert.Equal(items.Count, cached!.Value.Items.Count);
            Assert.Equal(Now, cached.Value.FetchedUtc);
            Assert.Equal(items[0].StartUtc, cached.Value.Items[0].StartUtc);

            repo.AddSpot(new Spot(Now, "KH8A", 24904.0, "IZ0XXX", "up 2", "Cluster"));
            Assert.Single(repo.RecentSpots("KH8A"));
            Assert.True(repo.MarkKnown("KH8A|2026"));
            Assert.False(repo.MarkKnown("KH8A|2026"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
