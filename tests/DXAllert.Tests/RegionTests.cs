using DXAllert.Core;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Prefetture giapponesi (WAJA) e province cinesi dal log.</summary>
public class RegionTests
{
    private static WorkedEntry Qso(string call, string state = "", string refs = "") =>
        new(call, "20m", "FT8", "", State: state, AwardRefs: refs);

    [Theory]
    [InlineData("JA7WLY", "3", "03")]    // Iwate (campo STATE senza zero iniziale)
    [InlineData("JI1CNA", "10", "10")]   // Tokyo: numerazione JARL, non ISO (13)
    [InlineData("JA8XYZ", "", "01")]     // distretto 8 = Hokkaido
    [InlineData("JA1XYZ", "", null)]     // distretto 1: più prefetture, non si può sapere
    [InlineData("JA1XYZ", "48", null)]
    public void JapanPrefectureFromStateOrCall(string call, string state, string? expected) =>
        Assert.Equal(expected, JapanPrefectures.Of(Qso(call, state)));

    [Fact]
    public void JapanPrefectureFromLog4OmJcc() =>
        Assert.Equal("08", JapanPrefectures.Of(Qso("JA0JWQ", refs: "JCC\t0801\t\t")));   // Niigata

    [Theory]
    [InlineData("BG7IDX", "GD")]
    [InlineData("BH4BNQ", "SH")]
    [InlineData("BD4UJ", "JS")]
    [InlineData("BY8GA", "CQ")]
    [InlineData("BG0CFD", "XJ")]
    [InlineData("B1Z", "BJ")]
    [InlineData("BA7YA", "HI")]
    public void ChinaProvinceFromCall(string call, string code)
    {
        var p = ChinaProvinces.Of(Qso(call), "China");
        Assert.Equal((code, true), p);
    }

    [Fact]
    public void ChinaProvinceFromStateTaiwanHongKongAndUnknown()
    {
        Assert.Equal(("YN", false), ChinaProvinces.Of(Qso("BG7IDX", "yn"), "China"));
        Assert.Equal(("TW", false), ChinaProvinces.Of(Qso("BV2A"), "Taiwan"));
        Assert.Equal(("HK", false), ChinaProvinces.Of(Qso("VR2XMT"), "Hong Kong"));
        Assert.Null(ChinaProvinces.Of(Qso("BG0ZZZ"), "China"));     // distretto 0, lettera fuori dagli intervalli
        Assert.Null(ChinaProvinces.Of(Qso("JA1AA"), "Japan"));
    }

    [Fact]
    public void AdifKeepsLog4OmReferences()
    {
        const string refs = """[{"AC":"JCC","R":"2501","SUB":[]},{"AC":"DXCC_QSL","R":"339"}]""";
        var e = Assert.Single(AdifParser.Parse($"<CALL:6>JG3FEA<BAND:3>20m<MODE:2>CW<APP_L4ONG_QSO_AWARD_REFERENCES:{refs.Length}>{refs}<EOR>"));
        Assert.Equal(new[] { new AwardRef("JCC", "2501", "", "") }, e.Refs);
        Assert.Equal("25", JapanPrefectures.Of(e));
    }
}
