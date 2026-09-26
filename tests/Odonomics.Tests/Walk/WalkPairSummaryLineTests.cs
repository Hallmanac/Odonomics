using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkPairSummaryLineTests
{
    [Fact]
    public void Format_OneDroppedReason_NamesItWithoutARedundantCount()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 9, known: 0, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 9 pages, 0 known from cards, 7 saved, 2 dropped (missing fields)", line);
    }

    [Fact]
    public void Format_KnownFromCards_SitsBesideVisitedSavedAndDroppedWithoutEnteringTheirSum()
    {
        var dropped = new DroppedBreakdown(MissingFields: 1, NoVin: 0, NotMatching: 0, Failed: 0);

        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 3, known: 146, saved: 2, dropped);

        Assert.Equal("carvana / Toyota Camry Hybrid: 3 pages, 146 known from cards, 2 saved, 1 dropped (missing fields)", line);
    }

    [Fact]
    public void Format_NothingDropped_OmitsTheParentheticalReasonClause()
    {
        var dropped = new DroppedBreakdown(0, 0, 0, 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 7, known: 0, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 7 pages, 0 known from cards, 7 saved, 0 dropped", line);
    }

    [Fact]
    public void Format_MixedDroppedReasons_NamesEachWithItsOwnCountInTheFixedOrder()
    {
        var dropped = new DroppedBreakdown(MissingFields: 3, NoVin: 0, NotMatching: 4, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 14, known: 0, saved: 7, dropped);

        Assert.Equal("cars.com / Honda Insight: 14 pages, 0 known from cards, 7 saved, 7 dropped (4 wrong model, 3 missing fields)", line);
    }

    [Fact]
    public void Format_SavedPlusEveryDroppedReasonAlwaysSumsToPages()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 0, Failed: 1);
        int saved = 5;

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: saved + dropped.Total, known: 0, saved, dropped);

        Assert.Equal("cars.com / Honda Insight: 9 pages, 0 known from cards, 5 saved, 4 dropped (2 missing fields, 1 no VIN, 1 failed to load)", line);
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
    public void DroppedCell_NewCarReason_IsWordedAsNewCarListing()
    {
        var dropped = new DroppedBreakdown(0, 0, 0, 0, NewCar: 3);

        Assert.Equal("new-car listing", WalkPairSummaryLine.DroppedCell(dropped));
        Assert.Equal("new-car listing", WalkOutcomeWording.DroppedReason(DetailPageOutcome.NewCar));
    }

    [Fact]
    public void Format_NewCarAlongsideTheOtherReasons_NamesEachWithItsOwnCountAndSumsToPages()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 3, Failed: 1, ExtractionFailed: 1, Repeat: 4, NewCar: 11);
        int saved = 12;

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Corolla Hybrid", pages: saved + dropped.Total, known: 0, saved, dropped);

        Assert.Equal(
            "cars.com / Toyota Corolla Hybrid: 35 pages, 0 known from cards, 12 saved, 23 dropped (3 wrong model, 11 new-car listing, 2 missing fields, 1 no VIN, 4 repeat, 1 failed to load, 1 extraction failed)",
            line);
    }

    [Fact]
    public void DroppedCell_SoldAndNoPriceReasons_AreWordedForWhatHappened()
    {
        Assert.Equal("listing sold", WalkPairSummaryLine.DroppedCell(new DroppedBreakdown(0, 0, 0, 0, Sold: 2)));
        Assert.Equal("no price listed", WalkPairSummaryLine.DroppedCell(new DroppedBreakdown(0, 0, 0, 0, NoPriceListed: 1)));
        Assert.Equal("listing sold", WalkOutcomeWording.DroppedReason(DetailPageOutcome.Sold));
        Assert.Equal("no price listed", WalkOutcomeWording.DroppedReason(DetailPageOutcome.NoPriceListed));
    }

    [Fact]
    public void Format_SoldAndNoPriceAlongsideTheOtherReasons_NamesEachWithItsOwnCountAndSumsToPages()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 3, Failed: 0, NewCar: 1, Sold: 4, NoPriceListed: 2);
        int saved = 5;

        string line = WalkPairSummaryLine.Format("autotrader", "Toyota", "Prius", pages: saved + dropped.Total, known: 0, saved, dropped);

        Assert.Equal(
            "autotrader / Toyota Prius: 18 pages, 0 known from cards, 5 saved, 13 dropped (3 wrong model, 1 new-car listing, 4 listing sold, 2 no price listed, 2 missing fields, 1 no VIN)",
            line);
    }

    [Fact]
    public void TableCell_SoldAndNoPrice_NameEachReasonInTheEndOfRunTable()
    {
        Assert.Equal("5 (3 listing sold, 2 no price listed)", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 0, 0, 0, Sold: 3, NoPriceListed: 2)));
    }

    [Fact]
    public void Plus_SoldAndNoPrice_AddAcrossPairs()
    {
        DroppedBreakdown total = new DroppedBreakdown(0, 0, 0, 0, Sold: 1, NoPriceListed: 2).Plus(new DroppedBreakdown(0, 0, 0, 0, Sold: 3, NoPriceListed: 4));

        Assert.Equal(4, total.Sold);
        Assert.Equal(6, total.NoPriceListed);
        Assert.Equal(10, total.Total);
    }

    [Fact]
    public void TableCell_OnlyNewCars_NamesTheReasonWithoutARedundantCount()
    {
        Assert.Equal("11 (new-car listing)", WalkPairSummaryLine.TableCell(new DroppedBreakdown(0, 0, 0, 0, NewCar: 11)));
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

    [Fact]
    public void Format_CamryExampleAtEightyColumns_WrapsAfterTheTally()
    {
        var dropped = new DroppedBreakdown(MissingFields: 10, NoVin: 0, NotMatching: 14, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Camry Hybrid", pages: 35, known: 0, saved: 11, dropped, width: 80);

        Assert.Equal(
            "cars.com / Toyota Camry Hybrid: 35 pages, 0 known from cards, 11 saved, 24 dropped\n    (14 wrong model, 10 missing fields)",
            line);
    }

    [Fact]
    public void Format_CamryExampleAtOneHundredTwentyColumns_StaysOnOneLine()
    {
        var dropped = new DroppedBreakdown(MissingFields: 10, NoVin: 0, NotMatching: 14, Failed: 0);

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Camry Hybrid", pages: 35, known: 0, saved: 11, dropped, width: 120);

        Assert.Equal(
            "cars.com / Toyota Camry Hybrid: 35 pages, 0 known from cards, 11 saved, 24 dropped (14 wrong model, 10 missing fields)",
            line);
    }

    [Fact]
    public void Format_OneReasonLineWiderThanEightyColumns_WrapsAfterTheTally()
    {
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 0, NotMatching: 0, Failed: 0, ExtractionFailed: 24);

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Corolla Hybrid", pages: 35, known: 0, saved: 11, dropped, width: 80);

        Assert.Equal(
            "cars.com / Toyota Corolla Hybrid: 35 pages, 0 known from cards, 11 saved, 24 dropped\n    (extraction failed)",
            line);
    }

    [Fact]
    public void Format_OneReasonLineAtOneHundredTwentyColumns_StaysOnOneLine()
    {
        var dropped = new DroppedBreakdown(MissingFields: 0, NoVin: 0, NotMatching: 0, Failed: 0, ExtractionFailed: 24);

        string line = WalkPairSummaryLine.Format("cars.com", "Toyota", "Corolla Hybrid", pages: 35, known: 0, saved: 11, dropped, width: 120);

        Assert.Equal(
            "cars.com / Toyota Corolla Hybrid: 35 pages, 0 known from cards, 11 saved, 24 dropped (extraction failed)",
            line);
    }

    [Fact]
    public void Format_LineExactlyTheConsoleWidth_StaysOnOneLine()
    {
        var dropped = new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0);
        string expected = "cars.com / Honda Insight: 9 pages, 0 known from cards, 7 saved, 2 dropped (missing fields)";

        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 9, known: 0, saved: 7, dropped, expected.Length);

        Assert.Equal(expected, line);
    }

    [Fact]
    public void Format_NothingDroppedAtANarrowWidth_NeverAddsAContinuationLine()
    {
        string line = WalkPairSummaryLine.Format("cars.com", "Honda", "Insight", pages: 7, known: 0, saved: 7, new DroppedBreakdown(0, 0, 0, 0), width: 20);

        Assert.Equal("cars.com / Honda Insight: 7 pages, 0 known from cards, 7 saved, 0 dropped", line);
    }

    [Fact]
    public void Format_ACappedPairWithNothingDropped_EndsWithCapped()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 30, known: 0, saved: 30, new DroppedBreakdown(0, 0, 0, 0), capped: true);

        Assert.Equal("carvana / Toyota Camry Hybrid: 30 pages, 0 known from cards, 30 saved, 0 dropped, capped", line);
    }

    [Fact]
    public void Format_ACappedPairWithADroppedBreakdown_PutsCappedAfterTheBreakdown()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 32, known: 0, saved: 30, new DroppedBreakdown(0, 0, 2, 0), capped: true);

        Assert.Equal("carvana / Toyota Camry Hybrid: 32 pages, 0 known from cards, 30 saved, 2 dropped (wrong model), capped", line);
    }

    [Fact]
    public void Format_ACappedPairWhoseLineWraps_KeepsCappedWithTheBreakdown()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 32, known: 0, saved: 30, new DroppedBreakdown(0, 0, 2, 0), width: 60, capped: true);

        Assert.Equal("carvana / Toyota Camry Hybrid: 32 pages, 0 known from cards, 30 saved, 2 dropped\n    (wrong model), capped", line);
    }

    [Fact]
    public void Format_APairWhoseLaterPageFailed_EndsWithWhichPageFailedAndNeverSaysCapped()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 40, known: 0, saved: 40, new DroppedBreakdown(0, 0, 0, 0), failedPage: 3);

        Assert.Equal("carvana / Toyota Camry Hybrid: 40 pages, 0 known from cards, 40 saved, 0 dropped, page 3 failed", line);
        Assert.DoesNotContain("capped", line);
    }

    [Fact]
    public void Format_APairWhoseLaterPageFailedAndWhoseLineWraps_KeepsThePageWithTheBreakdown()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 32, known: 0, saved: 30, new DroppedBreakdown(0, 0, 2, 0), width: 60, failedPage: 3);

        Assert.Equal("carvana / Toyota Camry Hybrid: 32 pages, 0 known from cards, 30 saved, 2 dropped\n    (wrong model), page 3 failed", line);
    }

    [Fact]
    public void Format_APairThatWasNotCapped_NeverSaysCapped()
    {
        string line = WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", pages: 30, known: 0, saved: 30, new DroppedBreakdown(0, 0, 0, 0));

        Assert.DoesNotContain("capped", line);
    }
}
