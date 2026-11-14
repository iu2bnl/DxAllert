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
        Assert.Equal(new WorkedEntry("3B8FA", "20m", "FT8", "Mauritius"), Entries[0]);
        Assert.Equal(new WorkedEntry("3B8FA", "15m", "CW", ""), Entries[1]);
    }

    [Fact]
    public void DerivesBandFromFrequencyAndPrefersSubmode()
    {
        Assert.Equal(new WorkedEntry("VK9DX/P", "20m", "USB", ""), Entries[2]);
        Assert.Equal(new WorkedEntry("IK1AB", "40m", "FT4", "Italy"), Entries[3]);
    }

    [Fact]
    public void WorksWithoutHeader()
    {
        var list = AdifParser.Parse("<CALL:5>ZL1AA<BAND:3>10m<MODE:3>SSB<EOR>");
        Assert.Equal(new WorkedEntry("ZL1AA", "10m", "SSB", ""), Assert.Single(list));
    }
}