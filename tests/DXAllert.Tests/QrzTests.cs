using DXAllert.Data;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Parser delle risposte del servizio XML di QRZ.com (formato della versione 1.36).</summary>
public class QrzTests
{
    private const string Head = "<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n<QRZDatabase version=\"1.36\" xmlns=\"http://xmldata.qrz.com\">\n";

    [Fact]
    public void ParsesLoginSession()
    {
        var (key, sub, error) = QrzClient.ParseSession(Head +
            "<Session><Key>2331uf894c4bd29f3923f3bacf02c532d7bd9</Key><Count>123</Count><SubExp>Wed Jan 1 12:34:03 2027</SubExp><GMTime>Sat Nov 14 13:36:17 2026</GMTime></Session></QRZDatabase>");
        Assert.Equal("2331uf894c4bd29f3923f3bacf02c532d7bd9", key);
        Assert.Equal("Wed Jan 1 12:34:03 2027", sub);
        Assert.Equal("", error);
    }

    [Fact]
    public void ParsesLoginError()
    {
        // Risposta reale a credenziali sbagliate.
        var (key, _, error) = QrzClient.ParseSession(Head +
            "<Session>\n<Error>Username/password incorrect </Error>\n<GMTime>Sat Nov 14 13:36:17 2026</GMTime>\n<Remark>cpu: 0.020s</Remark>\n</Session>\n</QRZDatabase>");
        Assert.Equal("", key);
        Assert.Equal("Username/password incorrect", error);
    }

    [Fact]
    public void ParsesCallsignRecord()
    {
        var (info, error) = QrzClient.ParseLookup(Head +
            "<Callsign><call>AA7BQ</call><aliases>N6UFT,KJ6RK</aliases><fname>FRED L</fname><name>LLOYD</name>" +
            "<addr1>24 W. Camelback Rd</addr1><addr2>Phoenix</addr2><state>AZ</state><zip>85013</zip><country>United States</country>" +
            "<lat>33.509665</lat><lon>-112.074142</lon><grid>DM33vm</grid><qslmgr>via bureau</qslmgr><email>fred@example.org</email>" +
            "<url>https://www.qrz.com/db/AA7BQ</url><image>https://cdn.example.org/aa7bq.jpg</image><eqsl>0</eqsl><mqsl>1</mqsl>" +
            "<cqzone>3</cqzone><ituzone>6</ituzone><lotw>1</lotw><iota>NA-001</iota></Callsign>" +
            "<Session><Key>abc</Key><Count>5</Count><SubExp>non-subscriber</SubExp></Session></QRZDatabase>");
        Assert.Equal("", error);
        Assert.NotNull(info);
        Assert.Equal("AA7BQ", info!.Call);
        Assert.Equal("FRED L LLOYD", info.Name);
        Assert.Equal("24 W. Camelback Rd, 85013 Phoenix AZ", info.Address);
        Assert.Equal("United States", info.Country);
        Assert.Equal("DM33vm", info.Grid);
        Assert.Equal(33.509665, info.Latitude);
        Assert.Equal(-112.074142, info.Longitude);
        Assert.Equal("3", info.CqZone);
        Assert.Equal("via bureau", info.QslManager);
        Assert.Equal("1", info.Lotw);
        Assert.Equal("0", info.Eqsl);
        Assert.Equal("https://cdn.example.org/aa7bq.jpg", info.ImageUrl);
        Assert.Equal("N6UFT,KJ6RK", info.Aliases);
    }

    [Theory]
    [InlineData("Not found: XX1XX")]
    [InlineData("Session Timeout")]
    [InlineData("Invalid session key")]
    public void ReportsLookupErrors(string message)
    {
        var (info, error) = QrzClient.ParseLookup(Head + $"<Session><Error>{message}</Error></Session></QRZDatabase>");
        Assert.Null(info);
        Assert.Equal(message, error);
    }
}