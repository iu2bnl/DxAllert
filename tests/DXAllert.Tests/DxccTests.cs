using DXAllert.Core;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Riconoscimento nominativo -> entità DXCC con il cty.dat reale.</summary>
public class DxccTests
{
    private static readonly CtyResolver Cty = CtyResolver.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "cty.dat")));

    [Fact]
    public void LoadsAllEntities() => Assert.InRange(Cty.Entities.Count, 330, 350);

    [Theory]
    [InlineData("IZ2ABC", "Italy", "EU", 15)]
    [InlineData("I1JQJ", "Italy", "EU", 15)]
    [InlineData("IT9ABC", "Italy", "EU", 15)] // Sicilia: solo WAEDC, per il DXCC è Italia
    [InlineData("IS0ABC", "Sardinia", "EU", 15)]
    [InlineData("KH8A", "American Samoa", "OC", 32)]
    [InlineData("H44MS", "Solomon Islands", "OC", 28)]
    [InlineData("3X4U", "Guinea", "AF", 35)]
    [InlineData("C8K", "Mozambique", "AF", 37)]
    [InlineData("FW3GT", "Wallis & Futuna Islands", "OC", 32)]
    [InlineData("VK9XY", "Christmas Island", "OC", 29)]
    [InlineData("W1AW", "United States", "NA", 5)]
    [InlineData("JA1ABC", "Japan", "AS", 25)]
    public void ResolvesPlainCallsigns(string call, string entity, string continent, int cq)
    {
        var e = Cty.Resolve(call);
        Assert.NotNull(e);
        Assert.Equal(entity, e!.Name);
        Assert.Equal(continent, e.Continent);
        Assert.Equal(cq, e.CqZone);
    }

    [Theory]
    [InlineData("3B9/M0CFW", "Rodriguez Island")]
    [InlineData("6W/I2YSB", "Senegal")]
    [InlineData("PJ6/DL8DYL", "Saba & St. Eustatius")]
    [InlineData("HR5/F2JD", "Honduras")]
    [InlineData("9N/S57MK", "Nepal")]
    [InlineData("M0CFW/3B9", "Rodriguez Island")]
    [InlineData("FT4YM/P", "Antarctica")]
    [InlineData("IZ2ABC/P", "Italy")]
    [InlineData("iz2abc/qrp", "Italy")]
    [InlineData("F/IZ2ABC", "France")]
    [InlineData("LU1DMZ/Z", "South Orkney Islands")] // nominativo esatto nel cty.dat
    public void ResolvesPortableCallsigns(string call, string entity) =>
        Assert.Equal(entity, Cty.Resolve(call)?.Name);

    [Theory]
    [InlineData("3Y0L", "Peter I", "Peter 1 Island")]
    [InlineData("3Y0L", "Peter I Island", "Peter 1 Island")]
    [InlineData("VP0SG", "South Georgia I", "South Georgia Island")]
    [InlineData("FW3GT", "Wallis and Futuna", "Wallis & Futuna Islands")]
    [InlineData("3B9/M0CFW", "Rodrigues I", "Rodriguez Island")] // nome diverso: decide il prefisso
    [InlineData("GB100TL", "Lundy Island", "England")]
    [InlineData("KH8A", "", "American Samoa")]
    public void EntityNameFromSourceWinsWhenItMatches(string call, string hint, string entity) =>
        Assert.Equal(entity, Cty.Resolve(call, hint)?.Name);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("IZ2ABC/MM")]
    [InlineData("QQ0QQ")]
    public void ReturnsNullWhenNoEntity(string? call) => Assert.Null(Cty.Resolve(call));

    [Fact]
    public void CoordinatesUseEastPositiveLongitude()
    {
        var italy = Cty.Resolve("I2XYZ")!;
        Assert.InRange(italy.Latitude, 40, 45);
        Assert.InRange(italy.Longitude, 9, 15);
        Assert.True(Cty.Resolve("JA1ABC")!.Longitude > 130);
        Assert.True(Cty.Resolve("W1AW")!.Longitude < -70);
    }

    [Theory]
    [InlineData("JN55", 45.5, 11.0)]
    [InlineData("JN45ol", 45.479, 9.208)]
    [InlineData("FN31pr", 41.729, -72.708)]
    public void LocatorToCoordinates(string locator, double lat, double lon)
    {
        var p = Geo.LocatorToLatLon(locator);
        Assert.NotNull(p);
        Assert.Equal(lat, p!.Value.Lat, 2);
        Assert.Equal(lon, p.Value.Lon, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("JN5")]
    [InlineData("ZZ99")]
    [InlineData("JN55zz")]
    public void InvalidLocator(string locator) => Assert.Null(Geo.LocatorToLatLon(locator));

    [Fact]
    public void DistanceAndBearing()
    {
        // Roma -> New York: circa 6890 km, azimut circa 300°.
        Assert.InRange(Geo.DistanceKm(41.9, 12.5, 40.71, -74.0), 6850, 6930);
        Assert.InRange(Geo.BearingDeg(41.9, 12.5, 40.71, -74.0), 295, 305);
        Assert.Equal(0, Geo.BearingDeg(0, 0, 10, 0), 3);
        Assert.Equal(90, Geo.BearingDeg(0, 0, 0, 10), 3);
    }

    [Theory]
    [InlineData(14025.0, "", "20m", "CW")]
    [InlineData(14195.0, "", "20m", "SSB")]
    [InlineData(14074.0, "", "20m", "FT8")]
    [InlineData(7047.5, "", "40m", "FT4")]
    [InlineData(21095.0, "RTTY up 2", "15m", "RTTY")]
    [InlineData(28490.0, "USB 59", "10m", "SSB")]
    [InlineData(144300.0, "", "", "")]
    public void BandAndMode(double khz, string comment, string band, string mode)
    {
        Assert.Equal(band, BandPlan.BandOf(khz));
        Assert.Equal(mode, BandPlan.ModeOf(khz, comment));
    }

    [Theory]
    [InlineData("By DL2GAC; 80-10m; mainly FT8, some SSB", "80-10m", "FT8 SSB")]
    [InlineData("five stations on 160-6 metres. CW, SSB, and RTTY", "160-6m", "CW SSB RTTY")]
    [InlineData("By N7QT fm Manihiki I; HF; mainly CW", "HF", "CW")]
    [InlineData("QRV on 40, 20 and 15m SSB", "40 20 15m", "SSB")]
    public void ExtractsBandsAndModesFromText(string text, string bands, string modes)
    {
        Assert.Equal(bands, InfoExtractor.Bands(text));
        Assert.Equal(modes, InfoExtractor.Modes(text));
    }

    [Fact]
    public void ExpandsBandRanges()
    {
        Assert.Equal(new[] { "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m" }, InfoExtractor.ExpandBands("80-10m"));
        Assert.Equal(new[] { "40m", "20m", "15m" }, InfoExtractor.ExpandBands("40 20 15m"));
        Assert.DoesNotContain("6m", InfoExtractor.ExpandBands("HF"));
    }
}
