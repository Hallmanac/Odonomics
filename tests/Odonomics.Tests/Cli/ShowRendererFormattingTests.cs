using Odonomics.Cli;
using Odonomics.Domain;

namespace Odonomics.Tests.Cli;

public class ShowRendererFormattingTests
{
    [Fact]
    public void PriceHistory_ConsecutiveEqualObservations_CollapseToOne()
    {
        Assert.Equal("$22,489", ShowRenderer.PriceHistory([22489m, 22489m]));
    }

    [Fact]
    public void PriceHistory_EqualObservationsBeforeAChange_CollapseAndKeepTheChange()
    {
        Assert.Equal("$22,489 -> $21,990", ShowRenderer.PriceHistory([22489m, 22489m, 21990m]));
    }

    [Fact]
    public void PriceHistory_ARepeatedPriceThatIsNotConsecutive_IsKept()
    {
        Assert.Equal("$22,489 -> $21,990 -> $22,489", ShowRenderer.PriceHistory([22489m, 21990m, 22489m]));
    }

    [Fact]
    public void RecallDate_DayFirstText_IsReformattedAsIso()
    {
        Assert.Equal("2019-07-24", ShowRenderer.RecallDate("24/07/2019"));
    }

    [Fact]
    public void RecallDate_MalformedText_IsReturnedUnchanged()
    {
        Assert.Equal("31/02/2019", ShowRenderer.RecallDate("31/02/2019"));
        Assert.Equal("last July", ShowRenderer.RecallDate("last July"));
        Assert.Equal("", ShowRenderer.RecallDate(""));
    }

    [Fact]
    public void DaysOnMarket_NoReportedFigure_FallsBackToTheCurrentSellerGroupsFirstSighting()
    {
        SellerGroupSummary earlier = Group(new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero), new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
        SellerGroupSummary current = Group(new(2025, 10, 16, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));
        DateTimeOffset now = new(2026, 9, 23, 8, 15, 0, TimeSpan.Zero);

        Assert.Equal("342", ShowRenderer.DaysOnMarket(null, [earlier, current], now));
    }

    [Fact]
    public void DaysOnMarket_ReportedFigure_WinsOverTheGroupFallback()
    {
        SellerGroupSummary current = Group(new(2025, 10, 16, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("88", ShowRenderer.DaysOnMarket(88, [current], new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DaysOnMarket_NoReportedFigureAndCurrentGroupLastSeenLongAgo_IsUnknown()
    {
        SellerGroupSummary past = Group(new(2025, 10, 16, 0, 0, 0, TimeSpan.Zero), new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal("(unknown)", ShowRenderer.DaysOnMarket(null, [past], new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DaysOnMarket_NoReportedFigureAndNoGroup_IsUnknown()
    {
        Assert.Equal("(unknown)", ShowRenderer.DaysOnMarket(null, [], new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DaysOnMarket_SameDealerRelistedAfterAGap_CountsFromTheRelisting()
    {
        // One group by dealer-name stem, but two separate listings eight months apart: the days on
        // market are those of the relisting, not of the first listing.
        List<VinHistoryPoint> points =
        [
            new("Dealer A", new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), 19394m, 40000),
            new("Dealer A", new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), 27995m, 69680),
            new("Dealer A", new(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero), 26995m, 69680),
        ];
        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        Assert.Equal("22", ShowRenderer.DaysOnMarket(null, groups, new(2026, 9, 23, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void DaysOnMarket_CountsCalendarDatesInTheSightingsOwnOffset()
    {
        // 2026-09-10 at +10:00 is 2026-09-09 in UTC, but the table prints 2026-09-10, so the count
        // must agree with the printed date.
        TimeSpan offset = TimeSpan.FromHours(10);
        SellerGroupSummary current = Group(new(2026, 9, 10, 0, 0, 0, offset), new(2026, 9, 10, 0, 0, 0, offset));

        Assert.Equal("13", ShowRenderer.DaysOnMarket(null, [current], new(2026, 9, 23, 0, 0, 0, offset)));
    }

    private static SellerGroupSummary Group(DateTimeOffset first, DateTimeOffset last) =>
        new(first, last, first, ["Dealer A"], 20000m, 20000m, 40000, 40000);
}
