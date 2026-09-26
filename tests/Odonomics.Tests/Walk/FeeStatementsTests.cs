using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the fee posture and itemized total a walk stores, from detail pages recorded on
/// 2026-09-26 (fixtures/walks). On every recorded page that itemizes fees, the headline price is the
/// printed total with those fees inside it (cars.com's "All-in total price" row, Autotrader's "Total
/// Price" beside "Dealer Fees Included"), so those pages read as all-in and never as fees to add on top.
/// No recorded page lists fees without saying they are included, so the itemized cases are the
/// recorded pages with that statement edited out, the way <see cref="CarvanaShippingTests"/> edits its
/// recorded page for the cases nothing recorded.</summary>
public class FeeStatementsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private static string WithoutLines(string page, params string[] lines) =>
        string.Join('\n', page.Split('\n').Where(line => !lines.Contains(line.Trim())));

    [Fact]
    public void ReadCarsCom_PagePrintingSellerHasNoExtraFees_IsAllIn()
    {
        FeeStatement statement = FeeStatements.ReadCarsCom(Fixture("carscom-detail-no-extra-fees.txt"));

        Assert.Equal(FeePostures.AllIn, statement.Posture);
        Assert.Null(statement.ItemizedTotal);
    }

    [Fact]
    public void ReadCarsCom_PagePrintingOnlyTheAllInSentence_IsAllIn()
    {
        string page = WithoutLines(Fixture("carscom-detail-no-extra-fees.txt"), "Seller has no extra fees");

        Assert.Equal(FeePostures.AllIn, FeeStatements.ReadCarsCom(page).Posture);
    }

    [Fact]
    public void ReadCarsCom_BreakdownWithFeeRowsAndAnAllInTotal_IsAllInBecauseTheHeadlinePriceAlreadyHoldsTheFees()
    {
        FeeStatement statement = FeeStatements.ReadCarsCom(Fixture("carscom-detail-price-breakdown.txt"));

        Assert.Equal(FeePostures.AllIn, statement.Posture);
        Assert.Equal(995m + 499m, statement.ItemizedTotal);
    }

    [Fact]
    public void ReadCarsCom_BreakdownWithFeeRowsAndNoTotalRow_IsItemizedWithTheSumOfTheFeeRows()
    {
        string page = WithoutLines(Fixture("carscom-detail-price-breakdown.txt"), "All-in total price", "$29,990");

        FeeStatement statement = FeeStatements.ReadCarsCom(page);

        Assert.Equal(FeePostures.Itemized, statement.Posture);
        Assert.Equal(995m + 499m, statement.ItemizedTotal);
    }

    [Fact]
    public void ReadCarsCom_PageThatDoesNotDiscloseFees_IsUnknownEvenThoughItTellsTheBuyerToConfirmTheAllInTotalPrice()
    {
        FeeStatement statement = FeeStatements.ReadCarsCom(Fixture("carscom-detail-fees-not-disclosed.txt"));

        Assert.Equal(FeePostures.Unknown, statement.Posture);
        Assert.Null(statement.ItemizedTotal);
    }

    [Fact]
    public void ReadCarsCom_PageWithOnlyTheTaxesAndRegistrationDisclaimer_IsUnknown()
    {
        Assert.Equal(FeePostures.Unknown, FeeStatements.ReadCarsCom("Used 2025 Toyota Camry XLE\n$29,990\nPrice does not include taxes, registration, or other mandatory government charges.").Posture);
    }

    [Fact]
    public void ReadAutotrader_PageWithFeeLinesAndDealerFeesIncluded_IsAllInBecauseTheTotalPriceIsTheHeadline()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-fees-included.txt"));

        Assert.Equal(FeePostures.AllIn, statement.Posture);
        Assert.Equal(598m + 1298m + 189m, statement.ItemizedTotal);
        Assert.Equal(25664m, statement.ListingPrice);
        Assert.Equal(27749m, statement.TotalPrice);
    }

    [Fact]
    public void ReadCarsCom_UndisclosedNoticeBeforeALaterListingsAllInStatement_IsUnknown()
    {
        string page = "Seller has not disclosed fees\nSimilar vehicles\nSeller has no extra fees";

        Assert.Equal(FeePostures.Unknown, FeeStatements.ReadCarsCom(page).Posture);
    }

    [Fact]
    public void ForAskingPrice_AllInPageWhoseStoredPriceIsTheListingPrice_IsItemizedBecauseTheFeesAreNotInIt()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-fees-included.txt"));

        FeeStatement corrected = statement.ForAskingPrice(25664m);

        Assert.Equal(FeePostures.Itemized, corrected.Posture);
        Assert.Equal(598m + 1298m + 189m, corrected.ItemizedTotal);
    }

    [Fact]
    public void ForAskingPrice_AllInPageWhoseStoredPriceIsTheTotal_StaysAllIn()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-fees-included.txt"));

        Assert.Equal(statement, statement.ForAskingPrice(27749m));
    }

    [Fact]
    public void ForAskingPrice_ItemizedPageWhoseStoredPriceIsTheTotal_IsAllInSoTheFeesAreNotCountedTwice()
    {
        string page = WithoutLines(Fixture("autotrader-detail-fees-included.txt"), "Dealer Fees Included");
        FeeStatement statement = FeeStatements.ReadAutotrader(page);
        Assert.Equal(FeePostures.Itemized, statement.Posture);

        FeeStatement corrected = statement.ForAskingPrice(27749m);

        Assert.Equal(FeePostures.AllIn, corrected.Posture);
        Assert.Equal(598m + 1298m + 189m, corrected.ItemizedTotal);
        Assert.Equal(FeePostures.Itemized, statement.ForAskingPrice(25664m).Posture);
    }

    [Fact]
    public void ForAskingPrice_StoredPriceMatchingNeitherFigure_OrNoStoredPrice_LeavesTheReadingAlone()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-fees-included.txt"));

        Assert.Equal(statement, statement.ForAskingPrice(26000m));
        Assert.Equal(statement, statement.ForAskingPrice(null));
    }

    [Fact]
    public void ForAskingPrice_CarsComBreakdownWhoseStoredPriceIsTheVehiclePrice_IsItemized()
    {
        FeeStatement statement = FeeStatements.ReadCarsCom(Fixture("carscom-detail-price-breakdown.txt"));

        Assert.Equal(FeePostures.Itemized, statement.ForAskingPrice(statement.ListingPrice).Posture);
    }

    [Fact]
    public void ReadAutotrader_PagePrintingNoAdditionalDealerFees_IsAllIn()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-no-additional-fees.txt"));

        Assert.Equal(FeePostures.AllIn, statement.Posture);
        Assert.Null(statement.ItemizedTotal);
    }

    [Fact]
    public void ReadAutotrader_FeeLinesWithNoStatementThatTheyAreIncluded_AreItemizedWithTheirSum()
    {
        string page = WithoutLines(Fixture("autotrader-detail-fees-included.txt"), "Total Price", "$27,749", "Dealer Fees Included");

        FeeStatement statement = FeeStatements.ReadAutotrader(page);

        Assert.Equal(FeePostures.Itemized, statement.Posture);
        Assert.Equal(598m + 1298m + 189m, statement.ItemizedTotal);
    }

    [Fact]
    public void ReadAutotrader_PageWithAListingPriceAndNoFeeStatement_IsUnknown()
    {
        FeeStatement statement = FeeStatements.ReadAutotrader(Fixture("autotrader-detail-no-fee-statement.txt"));

        Assert.Equal(FeePostures.Unknown, statement.Posture);
        Assert.Null(statement.ItemizedTotal);
    }

    [Fact]
    public void ReadAutotrader_SearchStylePageWhoseOtherCarsCarryDealerFeesIncluded_IsUnknownRatherThanBorrowingTheirBadge()
    {
        Assert.Equal(FeePostures.Unknown, FeeStatements.ReadAutotrader(Fixture("autotrader-search-style-page-with-badges.txt")).Posture);
    }

    [Fact]
    public void ReadFeeStatement_EachSiteReadsItsOwnPagesAndCarvanaReadsNone()
    {
        string carsCom = Fixture("carscom-detail-fees-not-disclosed.txt");
        string autotrader = Fixture("autotrader-detail-fees-included.txt");

        Assert.Equal(FeePostures.Unknown, WalkSites.CarsCom.ReadFeeStatement(carsCom)?.Posture);
        Assert.Equal(FeePostures.AllIn, WalkSites.Autotrader.ReadFeeStatement(autotrader)?.Posture);
        Assert.Null(WalkSites.Carvana.ReadFeeStatement(autotrader));
    }
}
