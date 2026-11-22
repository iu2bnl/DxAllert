using DXAllert.Core;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Lettura dei log ADIF per la colonna "Già collegata".</summary>
public class AdifTests
{
    private static readonly IReadOnlyList<WorkedEntry> Entries =
        AdifParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "sample.adi")));

    [Fact]
    public void SkipsHeaderAndIncompleteRecords() => Assert.Equal(4, Entries.Count);

    [Fact]
    public void ReadsCallBandModeInAnyCase()
    {
        Assert.Equal(new WorkedEntry("3B8FA", "20m", "FT8", "Mauritius"), Entries[0] with { Date = null });
        Assert.Equal(new WorkedEntry("3B8FA", "15m", "CW", ""), Entries[1] with { Date = null });
    }

    [Fact]
    public void DerivesBandFromFrequencyAndPrefersSubmode()
    {
        Assert.Equal(new WorkedEntry("VK9DX/P", "20m", "USB", ""), Entries[2] with { Date = null });
        Assert.Equal(new WorkedEntry("IK1AB", "40m", "FT4", "Italy"), Entries[3] with { Date = null });
    }

    [Theory]
    [InlineData("<STATE:2>MA<CNTY:12>MA,Middlesex", "MA", "Middlesex")]
    [InlineData("<STATE:2>IL<CNTY:9>IL, Logan", "IL", "Logan")]
    [InlineData("<STATE:2>NH<CNTY:12>Hillsborough", "NH", "Hillsborough")]
    [InlineData("<CNTY:16>FL,Miami-Dade", "FL", "Miami-Dade")]
    [InlineData("<STATE:2>wi", "WI", "")]
    [InlineData("<CNTY:21>FL,OSCEOLA // Osceola", "FL", "OSCEOLA")]   // formato Log4OM
    public void ReadsStateAndCounty(string fields, string state, string county)
    {
        var e = Assert.Single(AdifParser.Parse($"<CALL:4>W1AW<BAND:3>20m<MODE:2>CW{fields}<EOR>"));
        Assert.Equal(state, e.State);
        Assert.Equal(county, e.County);
    }

    [Fact]
    public void ReadsDateConfirmationsZonesIotaAndGrid()
    {
        var e = Assert.Single(AdifParser.Parse("<CALL:5>5R8IC<BAND:3>10m<MODE:3>FT8<QSO_DATE:8>20261121<QSL_RCVD:1>N" +
            "<LOTW_QSL_RCVD:1>Y<EQSL_QSL_RCVD:1>V<CQZ:2>39<ITUZ:2>53<IOTA:6>af-090<GRIDSQUARE:6>lh43ab<EOR>"));
        Assert.Equal(new DateTime(2026, 11, 21), e.Date);
        Assert.Equal(QslFlags.Lotw | QslFlags.Eqsl, e.Qsl);
        Assert.Equal((39, 53), (e.CqZone, e.ItuZone));
        Assert.Equal(("AF-090", "LH43AB"), (e.Iota, e.Grid));
    }

    [Fact]
    public void WorksWithoutHeader()
    {
        var list = AdifParser.Parse("<CALL:5>ZL1AA<BAND:3>10m<MODE:3>SSB<EOR>");
        Assert.Equal(new WorkedEntry("ZL1AA", "10m", "SSB", ""), Assert.Single(list));
    }
}