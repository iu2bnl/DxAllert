using DXAllert.Core;
using Xunit;

namespace DXAllert.Tests;

/// <summary>Regole delle date dei contest, confrontate con le date 2026 pubblicate da ARI e WA7BNM.</summary>
public class ContestTests
{
    private static ContestEvent Edition(string name, int year) =>
        ContestCatalog.All.Single(c => c.Name == name).Occurrences(year).Single();

    [Theory]
    [InlineData("ARI International DX Contest", 2026, 5, 2, 12)]   // "2 e 3 maggio" (ARI)
    [InlineData("Contest Sezioni ARI HF", 2026, 6, 13, 12)]        // "13 e 14 Giugno" (ARI)
    [InlineData("Contest ARI 40/80", 2026, 12, 12, 13)]            // "12 e 13 dicembre" (ARI)
    [InlineData("Alpe Adria VHF Contest", 2026, 8, 2, 6)]          // "domenica 2 agosto 2026" (ARI)
    [InlineData("LZ DX Contest", 2026, 11, 21, 12)]                // "21 - 22" novembre (calendario ARI, WA7BNM)
    [InlineData("JIDX SSB Contest", 2026, 11, 14, 7)]              // "14 - 15" novembre (calendario ARI)
    [InlineData("CQ Worldwide DX Contest, CW", 2026, 11, 28, 0)]   // "28 - 29" novembre (calendario ARI)
    public void AnnualRulesMatchPublishedDates(string name, int year, int month, int day, int hour)
    {
        var e = Edition(name, year);
        Assert.Equal(new DateTime(year, month, day, hour, 0, 0), e.StartUtc);
    }

    [Fact]
    public void LastFullWeekendSkipsSaturdayAtMonthEnd()
    {
        // Ottobre 2022 finisce di lunedì: ultimo fine settimana completo 29-30. Agosto 2026 finisce di lunedì 31: 29-30.
        Assert.Equal(new DateTime(2022, 10, 29), ContestDef.NthFullWeekend(2022, 10, -1));
        // Ottobre 2026 finisce di sabato 31: quel sabato non ha la domenica nel mese, quindi l'ultimo è il 24-25.
        Assert.Equal(new DateTime(2026, 10, 24), ContestDef.NthFullWeekend(2026, 10, -1));
        // Febbraio 2026: primo sabato 7 (domenica 1 febbraio non forma un fine settimana completo).
        Assert.Equal(new DateTime(2026, 2, 7), ContestDef.NthFullWeekend(2026, 2, 1));
    }

    [Fact]
    public void MonthlyVhfContestsUseWinterTimes()
    {
        var iac = ContestCatalog.All.Single(c => c.Name == "I.A.C. 144 MHz").Occurrences(2026).ToList();
        Assert.Equal(12, iac.Count);
        Assert.Equal(new DateTime(2026, 12, 1, 18, 0, 0), iac[11].StartUtc); // primo martedì, orario invernale
        Assert.Equal(new DateTime(2026, 6, 2, 17, 0, 0), iac[5].StartUtc);   // primo martedì, orario estivo
        Assert.Equal(TimeSpan.FromHours(4), iac[0].EndUtc - iac[0].StartUtc);
    }

    [Fact]
    public void Cq160StartsFridayEvening()
    {
        var e = Edition("CQ 160-Meter Contest, CW", 2027);
        Assert.Equal(DayOfWeek.Friday, e.StartUtc.DayOfWeek);
        Assert.Equal(22, e.StartUtc.Hour);
    }

    [Fact]
    public void ParsesWa7bnmIcs()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nDTSTART:20261121T120000Z\r\nDTEND:20261122T120000Z\r\n" +
                           "SUMMARY:LZ DX Contest\r\nDESCRIPTION:http://www.contestcalendar.com/weeklycont.php\r\n" +
                           "URL:http://www.contestcalendar.com/contestdetails.php?ref=187\r\nEND:VEVENT\r\n" +
                           "BEGIN:VEVENT\r\nDTSTART:20261123T000000Z\r\nDTEND:20261123T010000Z\r\nSUMMARY:K1USN Slow Speed\r\n Test\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var list = IcsParser.Parse(ics);
        Assert.Equal(2, list.Count);
        Assert.Equal(new IcsEvent("LZ DX Contest", new DateTime(2026, 11, 21, 12, 0, 0), new DateTime(2026, 11, 22, 12, 0, 0),
            "http://www.contestcalendar.com/contestdetails.php?ref=187"), list[0]);
        Assert.Equal("K1USN Slow SpeedTest", list[1].Summary);
    }

    [Fact]
    public void WeeklyFeedReplacesCatalogEditionAndKeepsArea()
    {
        var from = new DateTime(2026, 11, 20); var to = new DateTime(2026, 11, 24);
        var catalog = ContestCatalog.Between(from, to);
        Assert.Contains(catalog, e => e.Name == "LZ DX Contest");
        var weekly = new[]
        {
            new IcsEvent("LZ DX Contest", new DateTime(2026, 11, 21, 12, 0, 0), new DateTime(2026, 11, 22, 12, 0, 0), "http://x/lz"),
            new IcsEvent("K1USN Slow Speed Test", new DateTime(2026, 11, 23, 0, 0, 0), new DateTime(2026, 11, 23, 1, 0, 0), "http://x/k1usn"),
        };
        var merged = ContestCatalog.Merge(catalog, weekly);
        var lz = Assert.Single(merged, e => e.Name.Contains("LZ DX"));
        Assert.True(lz.FromWeeklyFeed);
        Assert.Equal(ContestRegion.Europa, lz.Region);
        Assert.Equal("http://lzdx.bfra.bg/rulesen.html", lz.Url);
        Assert.Equal(ContestRegion.Altro, Assert.Single(merged, e => e.Name.StartsWith("K1USN")).Region);
    }

    [Theory]
    [InlineData("ARRL Sweepstakes Contest, SSB", ContestRegion.Altro)]
    [InlineData("RSGB 1.8 MHz Contest", ContestRegion.Altro)]
    [InlineData("CQ Worldwide DX Contest, CW", ContestRegion.Mondo)]
    [InlineData("LZ DX Contest", ContestRegion.Europa)]
    public void LookupByNameOnlyWhenUnambiguous(string name, ContestRegion region) =>
        Assert.Equal(region, ContestCatalog.Lookup(name).Region);

    [Theory]
    [InlineData("CQ Worldwide DX Contest, CW", "CQ World-Wide DX Contest, CW", true)]
    [InlineData("CQ WW WPX Contest, SSB", "CQ WW WPX Contest, SSB", true)]
    [InlineData("CQ Worldwide DX Contest, CW", "CQ Worldwide DX Contest, SSB", false)]
    [InlineData("ARRL 10-Meter Contest", "ARRL 160-Meter Contest", false)]
    public void ComparesContestNames(string a, string b, bool same) => Assert.Equal(same, ContestCatalog.SameName(a, b));
}
