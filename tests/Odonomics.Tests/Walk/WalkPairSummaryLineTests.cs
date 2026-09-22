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
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 3, NotMatching: 2, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 12, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 12 pages, 7 saved, 5 dropped (3 no VIN, 2 wrong model)", line);
    }

    [Fact]
    public void Format_SavedPlusEveryDroppedReasonAlwaysSumsToPages()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 0, Failed: 1);
        int saved = 5;

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: saved + dropped.Total, saved, dropped);

        Assert.Equal("cars.com / Honda Insight: 9 pages, 5 saved, 4 dropped (2 missing fields, +2)", line);
    }

    [Theory]
    [InlineData("cars.com", "Toyota", "Corolla Hybrid", 22, 10, 0, 1, 11, 0)]
    [InlineData("cars.com", "Toyota", "Corolla Hybrid", 22, 10, 2, 1, 8, 1)]
    [InlineData("cars.com", "Honda", "Insight", 22, 10, 0, 1, 11, 0)]
    [InlineData("carvana", "Toyota", "Camry Hybrid", 24, 12, 2, 1, 8, 1)]
    public void Format_NeverExceedsEightyColumns(
        string site, string make, string model, int pages, int saved,
        int missingFields, int noVin, int notMatching, int failed)
    {
        var dropped = new DroppedBreakdown(missingFields, noVin, notMatching, failed);

        string line = WalkPairSummaryLine.Format(site, make, model, pages, saved, dropped);

        Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\"");
    }

    [Fact]
    public void Format_WhenTheFullBreakdownWouldOverflow_KeepsWhatFitsAndFoldsTheRestIntoAPlusCount()
    {
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 1, NotMatching: 11, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 22, saved: 10, dropped);

        Assert.Equal("cars.com / Honda Insight: 22 pages, 10 saved, 12 dropped (1 no VIN, +1)", line);
    }

    [Fact]
    public void Format_WhenNotEvenOneReasonFits_OmitsTheWholeParentheticalButKeepsTheTotal()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 8, Failed: 1);

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Corolla Hybrid", pages: 22, saved: 10, dropped);

        Assert.Equal("cars.com / Toyota Corolla Hybrid: 22 pages, 10 saved, 12 dropped", line);
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
    public void DroppedCell_BoundedWidth_KeepsWhatFitsAndFoldsTheRestIntoAPlusCount()
    {
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 1, NotMatching: 11, Failed: 0);

        Assert.Equal("1 no VIN, +1", WalkPairSummaryLine.DroppedCell(dropped, maxWidth: 18));
    }

    [Fact]
    public void DroppedCell_BoundedWidth_IsEmptyWhenNotEvenOneReasonFits()
    {
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 0, NotMatching: 0, Failed: 2);

        Assert.Equal("", WalkPairSummaryLine.DroppedCell(dropped, maxWidth: 5));
    }

    [Fact]
    public void TableCell_NothingDropped_IsJustTheZero()
    {
        Assert.Equal("0", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 0, 0, 0), maxWidth: 15));
    }

    [Fact]
    public void TableCell_WithRoomToSpare_NamesTheOneReason()
    {
        Assert.Equal("3 (no VIN)", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 3, 0, 0), maxWidth: 15));
    }

    [Fact]
    public void TableCell_WhenTheBreakdownWouldOverflowTheColumn_FallsBackToJustTheTotal()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 8, Failed: 1);

        Assert.Equal("12", WalkPairSummaryLine.TableCell(dropped, maxWidth: 15));
    }
}
