using DXAllert.Core;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Diplomi: elenchi dei regolamenti, prefissi WPX, conteggi.</summary>
public class AwardTests
{
    private static readonly CtyResolver Cty = CtyResolver.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "cty.dat")));
    private static readonly IReadOnlyList<AwardDef> Catalog = AwardCatalog.Build(Cty);

    private static AwardDef Def(string id) => Catalog.Single(a => a.Id == id);

    private static AwardQso Qso(string call, string band = "20m", string mode = "CW", DateTime? date = null) =>
        new(new WorkedEntry(call, band, mode, "", Date: date), Cty.Resolve(call), Cty.Wae.Resolve(call), Cty.Resolve(Callsign.Base(call)));

    private static AwardResult Eval(string id, params AwardQso[] qsos) => AwardEngine.Evaluate(Def(id), qsos);

    [Fact]
    public void EveryListedEntityExistsInCtyDat()
    {
        var names = new HashSet<string>(Cty.Wae.Entities.Select(e => e.Name));
        var unknown = Catalog.Where(a => a.Items is not null)
            .SelectMany(a => a.Items!.SelectMany(i => i.Entities).Select(n => $"{a.Id}: {n}"))
            .Where(s => !names.Contains(s[(s.IndexOf(": ") + 2)..])).ToList();
        Assert.Empty(unknown);
    }

    [Theory]
    [InlineData("wsh", 100)]
    [InlineData("wnh", 240)]
    [InlineData("darc-wae", 73)]
    [InlineData("raag-europe", 73)]
    [InlineData("ari-cdm", 32)]
    [InlineData("wacs", 46)]
    [InlineData("rca-tpa", 22)]
    [InlineData("formula1", 22)]
    [InlineData("darc-waeu", 27)]
    [InlineData("euro-countries", 19)]
    [InlineData("wanc", 11)]
    public void ListsHaveTheCountsOfTheRules(string id, int count) => Assert.Equal(count, Def(id).Items!.Count);

    [Theory]
    [InlineData("IK3GER", "IK3")]
    [InlineData("3DA0X", "3DA0")]
    [InlineData("HF80ABC", "HF80")]
    [InlineData("W1AW/4", "W4")]
    [InlineData("CT1AAA/CT3", "CT3")]
    [InlineData("DL1AB/OZ", "OZ0")]
    [InlineData("IS0/IK3GER/P", "IS0")]
    [InlineData("RAEM", "RA0")]
    public void WpxPrefixes(string call, string prefix) => Assert.Equal(prefix, Callsign.WpxPrefix(call));

    [Theory]
    [InlineData("IS0/IK3GER/P", "IK3GER")]
    [InlineData("IT9/HB9AAA", "HB9AAA")]
    [InlineData("VK9DX/P", "VK9DX")]
    public void BaseCalls(string call, string expected) => Assert.Equal(expected, Callsign.Base(call));

    [Fact]
    public void W100ICountsItalianOperatorsOnceAndSkipsForeignersAndSanMarino()
    {
        var r = Eval("w100i", Qso("IK3GER"), Qso("IS0/IK3GER/P"), Qso("IT9/HB9AAA"), Qso("T70A"), Qso("HV0A"), Qso("IZ1ABC"));
        Assert.Equal(2, r.Progress);
        Assert.Equal(new[] { "IK3GER", "IZ1ABC" }, r.Items.Select(i => i.Key));
    }

    [Fact]
    public void WaeUsesWaeOnlyCountriesAndBandPoints()
    {
        var r = Eval("darc-wae", Qso("IT9ABC", "20m"), Qso("IT9ABC", "40m"), Qso("IK3GER", "20m"), Qso("W1AW", "20m"));
        Assert.Equal(2, r.Progress);       // Sicilia e Italia sono paesi WAE diversi; W1AW non conta
        Assert.Equal(3, r.Points);
        Assert.Contains(r.Items, i => i.Key == "Sicily" && i.Counts);
    }

    [Fact]
    public void BalkanNeedsTwoStationsPerCountry()
    {
        var r = Eval("raag-balkan", Qso("ZA1A"), Qso("ZA1B"), Qso("LZ1A"));
        Assert.Equal(1, r.Progress);
        Assert.False(r.Items.Single(i => i.Key == "Bulgaria").Counts);
    }

    [Fact]
    public void RequiredCountryBlocksTheLevel()
    {
        var qsos = Enumerable.Range(0, 5).Select(_ => 0).Select((_, i) => Qso(new[] { "OH1A", "SM5A", "LA1A", "OZ1A", "OY1A" }[i])).ToArray();
        var without = Eval("wanc", qsos);
        Assert.Null(without.Reached);
        Assert.Equal(new[] { "Iceland" }, without.MissingRequired);
        var with = Eval("wanc", qsos.Append(Qso("TF3A")).ToArray());
        Assert.Equal("Classe C", with.Reached?.Label);
    }

    [Fact]
    public void BestBandsCountsTheBestBands()
    {
        var calls = new[] { "5R8A", "JA1A", "IK3GER", "W1AW", "VK2A", "PY1A" };   // sei continenti
        var qsos = calls.SelectMany(c => new[] { "80m", "40m", "20m", "15m", "10m" }.Select(b => Qso(c, b))).ToArray();
        var r = Eval("5bwac", qsos);
        Assert.Equal(30, r.Progress);
        Assert.Equal("5BWAC", r.Reached?.Label);
        Assert.Equal(28, Eval("5bwac", qsos.Where(q => !(q.Call == "VK2A" && q.Band is "80m" or "10m")).ToArray()).Progress);
    }

    [Fact]
    public void DateLimitIsApplied()
    {
        var r = Eval("w100om", Qso("OM3A", date: new DateTime(1992, 12, 31)), Qso("OM3B", date: new DateTime(1993, 1, 1)));
        Assert.Equal(1, r.Progress);
    }

    [Fact]
    public void EuroCountriesSpelling()
    {
        var calls = new[] { "I1AAE", "I1AAE", "IK1AU", "IK1BU", "IZ1AR", "IZ1BR", "IZ1AO", "IZ1BO", "IZ1AC", "IZ1AN", "IZ1AT", "IZ1AI", "IZ1BE" };
        var missingS = AwardCatalog.SpellEuroCountries(calls.Select(c => Qso(c)).ToList());
        Assert.False(missingS.Done);
        Assert.Contains("S", missingS.Text);
        Assert.True(AwardCatalog.SpellEuroCountries(calls.Append("IZ1AS").Select(c => Qso(c)).ToList()).Done);
    }
}
