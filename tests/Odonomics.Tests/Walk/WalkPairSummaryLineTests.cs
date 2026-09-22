using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkPairSummaryLineTests
{
    [Fact]
    public void Format_OneDroppedReason_MatchesTheReportedShape()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 9, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 9 pages, 7 saved, 2 dropped (missing fields)", line);
    }

    [Fact]
    public void Format_NothingDropped_OmitsTheParentheticalReasonClause()
    {
        var dropped = new DroppedBreakdown(0, 0, 0, 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 7, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 7 pages, 7 saved, 0 dropped", line);
    }

    [Fact]
    public void Format_MultipleDroppedReasons_NamesEachWithItsOwnCountInAFixedOrder()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 3, Failed: 1);

        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 15, saved: 8, dropped);

        Assert.Equal(
            "carvana / Toyota Camry Hybrid: 15 pages, 8 saved, 7 dropped (2 missing fields, 1 no VIN, 3 wrong model, 1 failed to load)",
            line);
    }

    [Fact]
    public void Format_SavedPlusEveryDroppedReasonAlwaysSumsToPages()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 0, Failed: 1);
        int saved = 5;

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: saved + dropped.Total, saved, dropped);

        Assert.Equal("cars.com / Honda Insight: 9 pages, 5 saved, 4 dropped (2 missing fields, 1 no VIN, 1 failed to load)", line);
    }

    [Fact]
    public void DroppedCell_NoneDropped_IsEmpty()
    {
        Assert.Equal("", WalkPairSummaryLine.DroppedCell(new DroppedBreakdown(0, 0, 0, 0)));
    }

    [Fact]
    public void DroppedCell_ExactlyOneReason_OmitsItsRedundantCount()
    {
        Assert.Equal("no VIN", WalkPairSummaryLine.DroppedCell(new DroppedBreakdown(0, 3, 0, 0)));
    }
}
