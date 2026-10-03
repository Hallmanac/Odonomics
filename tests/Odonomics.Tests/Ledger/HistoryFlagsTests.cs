using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class HistoryFlagsTests
{
    private static readonly DateTimeOffset Seen = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static PostingEntity Posting(PostingHistory history, string source = "cargurus", int daysAgo = 0) => new()
    {
        VehicleVin = "4T1DAACK8SU095738",
        Source = source,
        Url = $"https://example.com/{source}/{daysAgo}",
        FirstSeen = Seen.AddDays(-daysAgo),
        LastSeen = Seen.AddDays(-daysAgo),
        History = history,
    };

    private static VehicleEntity Vehicle(params PostingEntity[] postings) => new()
    {
        Vin = "4T1DAACK8SU095738",
        Year = 2025,
        Make = "Toyota",
        Model = "Camry Hybrid",
        Mileage = 49336,
        FirstSeen = Seen,
        LastSeen = Seen,
        Postings = [.. postings],
    };

    [Fact]
    public void For_FrameDamageAndRentalUseStated_RaisesBothTagsWithTheirStatements()
    {
        IReadOnlyList<RedFlag> flags = HistoryFlags.For(Vehicle(Posting(new PostingHistory("Clean title", 0, 1, "Reported as previous rental vehicle", "Frame damage reported"))));

        Assert.Equal(["frame-damage", "rental-history"], flags.Select(f => f.ShortTag));
        Assert.Equal(
            "the listing says frame damage was reported: cargurus states \"Frame damage reported\"; a frame-damaged car is a safety risk, so confirm it with the seller and an inspection before buying",
            flags[0].Detail);
        Assert.Equal(
            "the listing's history summary says rental or fleet use: cargurus states \"Reported as previous rental vehicle\"; ask how the car was used and maintained before buying",
            flags[1].Detail);
    }

    [Fact]
    public void For_TwoAccidentsStated_NamesTheCount()
    {
        RedFlag flag = Assert.Single(HistoryFlags.For(Vehicle(Posting(new PostingHistory("Clean title", 2, 1)))));

        Assert.Equal("accident-reported", flag.ShortTag);
        Assert.Equal("the listing's history summary says so: cargurus reports 2 accidents; ask for the full history report and the repair records before buying", flag.Detail);
    }

    [Fact]
    public void For_OneAccidentStated_NamesItInTheSingular() =>
        Assert.Contains("cargurus reports 1 accident;", Assert.Single(HistoryFlags.For(Vehicle(Posting(new PostingHistory(AccidentCount: 1))))).Detail);

    [Fact]
    public void For_FleetUseStatement_RaisesRentalHistory()
    {
        RedFlag flag = Assert.Single(HistoryFlags.For(Vehicle(Posting(new PostingHistory(UseStatement: "Reported as corporate leased vehicle")))));

        Assert.Equal("rental-history", flag.ShortTag);
        Assert.Contains("\"Reported as corporate leased vehicle\"", flag.Detail);
    }

    [Fact]
    public void For_CleanSummaryWithZeroAccidents_RaisesNothing() =>
        Assert.Empty(HistoryFlags.For(Vehicle(Posting(new PostingHistory("Clean title", 0, 1)))));

    [Fact]
    public void For_NothingStated_RaisesNothing() =>
        Assert.Empty(HistoryFlags.For(Vehicle(Posting(PostingHistory.None), Posting(PostingHistory.None, "cars.com", 1))));

    [Fact]
    public void For_SameSourceStatingADifferentCountLater_NamesTheNewestCountOnly()
    {
        VehicleEntity vehicle = Vehicle(
            Posting(new PostingHistory(AccidentCount: 1), daysAgo: 10),
            Posting(new PostingHistory(AccidentCount: 2), daysAgo: 1));

        Assert.Contains("cargurus reports 2 accidents;", Assert.Single(HistoryFlags.For(vehicle)).Detail);
        Assert.DoesNotContain("1 accident", HistoryFlags.For(vehicle)[0].Detail);
    }

    [Fact]
    public void For_NewestPostingNoLongerStatingACount_KeepsTheOlderStatedCount()
    {
        VehicleEntity vehicle = Vehicle(
            Posting(new PostingHistory(AccidentCount: 1), daysAgo: 10),
            Posting(PostingHistory.None, daysAgo: 1));

        Assert.Contains("cargurus reports 1 accident;", Assert.Single(HistoryFlags.For(vehicle)).Detail);
    }

    [Fact]
    public void For_SeveralPostingsStatingTheSameFrameDamage_NamesTheSourceOnce()
    {
        VehicleEntity vehicle = Vehicle(
            Posting(new PostingHistory(FrameDamageStatement: "Frame damage reported"), daysAgo: 4),
            Posting(new PostingHistory(FrameDamageStatement: "Frame damage reported"), daysAgo: 1));

        RedFlag flag = Assert.Single(HistoryFlags.For(vehicle));

        Assert.Equal(1, flag.Detail.Split("cargurus states").Length - 1);
    }
}
