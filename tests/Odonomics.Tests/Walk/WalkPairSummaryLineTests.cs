using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkPairSummaryLineTests
{
    [Fact]
    public void Format_OneDroppedReason_NamesItWithoutARedundantCount()
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
    public void Format_MixedDroppedReasons_NamesEachWithItsOwnCountInTheFixedOrder()
    {
        var dropped = new DroppedBreakdown(MissingFields: 3, NoVin: 0, NotMatching: 4, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 14, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 14 pages, 7 saved, 7 dropped (4 wrong model, 3 missing fields)", line);
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
    public void DroppedCell_NamesEveryNonZeroReasonInFixedOrder_WrongModelFirstThroughFailedToLoadLast()
    {
        var dropped = new DroppedBreakdown(MissingFields: 3, NoVin: 2, NotMatching: 5, Failed: 1, ExtractionFailed: 0, Repeat: 4);

        Assert.Equal(
            "5 wrong model, 3 missing fields, 2 no VIN, 4 repeat, 1 failed to load",
            WalkPairSummaryLine.DroppedCell(dropped));
    }

    [Fact]
    public void DroppedCell_ExtractionFailedReason_IsWordedDistinctlyFromFailedToLoad()
    {
        var extractionFailed = new DroppedBreakdown(0, 0, 0, Failed: 0, ExtractionFailed: 4);
        var pageLoadFailed = new DroppedBreakdown(0, 0, 0, Failed: 4, ExtractionFailed: 0);

        Assert.Equal("extraction failed", WalkPairSummaryLine.DroppedCell(extractionFailed));
        Assert.Equal("failed to load", WalkPairSummaryLine.DroppedCell(pageLoadFailed));
    }

    [Fact]
    public void DroppedCell_RepeatReason_IsWordedAsRepeat()
    {
        var dropped = new DroppedBreakdown(0, 0, 0, 0, ExtractionFailed: 0, Repeat: 2);

        Assert.Equal("repeat", WalkPairSummaryLine.DroppedCell(dropped));
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

    [Fact]
    public void TableCell_NothingDropped_IsJustTheZero()
    {
        Assert.Equal("0", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 0, 0, 0)));
    }

    [Fact]
    public void TableCell_OneDroppedReason_NamesItWithoutARedundantCount()
    {
        Assert.Equal("3 (no VIN)", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 3, 0, 0)));
    }

    [Fact]
    public void TableCell_MixedDroppedReasons_NamesEachWithItsOwnCountInTheFixedOrder()
    {
        var dropped = new DroppedBreakdown(MissingFields: 3, NoVin: 0, NotMatching: 4, Failed: 0);

        Assert.Equal("7 (4 wrong model, 3 missing fields)", WalkPairSummaryLine.TableCell(dropped));
    }
}
