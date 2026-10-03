using Odonomics.Cli;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

public class ShowRendererHistorySummaryTests
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

    [Fact]
    public void HistorySummaryLines_PostingWithAStatedSummary_NamesTheSourceAndEveryField() =>
        Assert.Equal(
            ["  cargurus (last seen 2026-10-03): Clean title, 0 accidents reported, 1 previous owner, Reported as previous rental vehicle, Frame damage reported"],
            ShowRenderer.HistorySummaryLines([Posting(new PostingHistory("Clean title", 0, 1, "Reported as previous rental vehicle", "Frame damage reported"))]));

    [Fact]
    public void HistorySummaryLines_PostingStatingNothing_IsLeftOutAndTheVehicleSaysNoListingStatedOne() =>
        Assert.Equal(
            ["  none stated by any listing (only the CarGurus walk reads one)"],
            ShowRenderer.HistorySummaryLines([Posting(PostingHistory.None, "cars.com")]));

    [Fact]
    public void HistorySummaryLines_SeveralPostings_AreNewestFirst() =>
        Assert.Equal(
            [
                "  cargurus (last seen 2026-10-02): 2 accidents reported",
                "  cargurus (last seen 2026-09-23): 1 accident reported",
            ],
            ShowRenderer.HistorySummaryLines([
                Posting(new PostingHistory(AccidentCount: 1), daysAgo: 10),
                Posting(PostingHistory.None, "cars.com", daysAgo: 5),
                Posting(new PostingHistory(AccidentCount: 2), daysAgo: 1),
            ]));
}
