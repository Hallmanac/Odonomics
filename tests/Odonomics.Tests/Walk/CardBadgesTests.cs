using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Card texts here are cut from the search pages the walk recorded on 2026-09-26
/// (walks/cars.com/20260926-121057/, walks/autotrader/20260926-121057/ and walks/carvana/20260926-121057/,
/// each under corolla-hybrid, prius or camry-hybrid). A carvana or autotrader badge prints ahead of the
/// card's title in the page text, so it sits at the top of its card.</summary>
public class CardBadgesTests
{
    private const string CarsComGreatDealHighDemand = """
        $21,202

        $349
        48,278 mi.
        Est. $385/mo
        Used 2023 Toyota Corolla Hybrid LE
        Great Deal
        High Demand

        Sport Mazda South

        4.9
        Orlando, FL (21 mi)
        Check Availability
        """;

    private const string CarsComGoodDeal = """
        $22,075

        45,129 mi.
        Est. $401/mo
        Used 2021 Toyota Corolla Hybrid SE
        Good Deal

        AutoNation USA Sanford

        3.3
        Sanford, FL (26 mi)
        Check Availability
        """;

    private const string CarsComFairDealWithAward = """
        $36,256

        $1.9K
        37,921 mi.
        Est. $658/mo
        Certified 2026 Toyota Camry XSE
        Fair Deal

        Kelly Ford

        4.8
        Melbourne, FL (38 mi)
        Check Availability
        """;

    private const string CarsComRatingOnly = """
        $23,999

        22,496 mi.
        Est. $436/mo
        Used 2019 Toyota Prius Limited

        Southern Trust Auto Group

        2.3
        Winter Garden, FL (30 mi)
        Check Availability
        """;

    private const string CarsComNoBadgeAndNoRating = """
        $23,999

        22,496 mi.
        Est. $436/mo
        Used 2019 Toyota Prius Limited

        Southern Trust Auto Group

        Winter Garden, FL (30 mi)
        Check Availability
        """;

    private const string AutotraderGreatPrice = """
        Used
        2022 Toyota Prius
        Limited
        9K mi
         Hybrid
        31,572
        Great Price
        Dealer Fees Included
        CITY KIA CARE CERTIFIED!
        """;

    private const string AutotraderGoodPriceOnlinePaperworkPriceDrop = """
        Price Drop
        Used
        2025 Toyota Camry
        LE
        11K mi
         Hybrid
        26,175
        See payment
        Good Price
        Dealer Fees Included
        No Accidents
        Sport Mazda
        20.39 mi. away
        (689) 273-8731
        Online Paperwork
        Check Availability
        """;

    private const string AutotraderNoBadge = """
        Used
        2025 Toyota Prius
        LE
        17K mi
         Hybrid
        29,524
        Dealer Fees Included
        """;

    private const string AutotraderHighDemandCard = """
        High Demand
        Toyota Gold Certified
        2026 Toyota Corolla
        SE
        2K mi
         Hybrid
        27,184
        See payment
        Great Price
        Dealer Fees Included
        No Accidents
        Parks Toyota of Deland
        35.92 mi. away
        (386) 490-8106
        Check Availability
        """;

    private const string CarvanaFreeShipping = """
        2024 Toyota Corolla Hybrid

        LE

        46k miles
        Current price:
        $24,590
        $440/mo
        estimated
        $0 cash down
        Free shipping
        Get it tomorrow
        """;

    private const string CarvanaPriceDropWithShippingFee = """
        Price Drop

        2025 Toyota Corolla Hybrid

        LE

        32k miles
        Current price:
        $25,990
        Original price:
        was
        $26,590
        $465/mo
        estimated
        $0 cash down
        $690 shipping
        Get it Monday
        """;

    private const string CarvanaPriceDropGreatDeal = """
        Price Drop
        Great Deal

        2021 Toyota Camry Hybrid

        XLE

        27k miles
        Current price:
        $28,990
        Original price:
        was
        $29,590
        $518/mo
        estimated
        $0 cash down
        $1,290 shipping
        Get it Wednesday
        """;

    private const string CarvanaRecentTag = """
        Recent

        2024 Toyota Corolla Hybrid

        LE

        5.8k miles
        Current price:
        $27,990
        $458/mo
        estimated
        $0 cash down
        $690 shipping
        Get it Monday
        """;

    private static void AssertBadges(IReadOnlyDictionary<string, string> actual, params (string Name, string Value)[] expected) =>
        Assert.Equal(
            expected.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray(),
            actual.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => (a.Key, a.Value)).ToArray());

    [Fact]
    public void CarsCom_ReadsTheDealBadgeTheDemandBadgeAndTheDealerRating()
    {
        AssertBadges(
            WalkSites.CarsCom.ReadCardBadges(CarsComGreatDealHighDemand),
            (PostingAttributeNames.Deal, "Great Deal"),
            (PostingAttributeNames.Demand, "High Demand"),
            (PostingAttributeNames.DealerRating, "4.9"));
    }

    [Fact]
    public void CarsCom_ReadsAGoodDealWithNoDemandBadge()
    {
        AssertBadges(
            WalkSites.CarsCom.ReadCardBadges(CarsComGoodDeal),
            (PostingAttributeNames.Deal, "Good Deal"),
            (PostingAttributeNames.DealerRating, "3.3"));
    }

    [Fact]
    public void CarsCom_ReadsAFairDeal()
    {
        AssertBadges(
            WalkSites.CarsCom.ReadCardBadges(CarsComFairDealWithAward),
            (PostingAttributeNames.Deal, "Fair Deal"),
            (PostingAttributeNames.DealerRating, "4.8"));
    }

    [Fact]
    public void CarsCom_ACardWithARatingAndNoBadgeRecordsOnlyTheRating()
    {
        AssertBadges(
            WalkSites.CarsCom.ReadCardBadges(CarsComRatingOnly),
            (PostingAttributeNames.DealerRating, "2.3"));
    }

    [Fact]
    public void CarsCom_ACardWithNoBadgeAndNoRatingRecordsNothing()
    {
        Assert.Empty(WalkSites.CarsCom.ReadCardBadges(CarsComNoBadgeAndNoRating));
    }

    [Fact]
    public void CarsCom_DoesNotTakeAMileageOrAPriceForARating()
    {
        Assert.False(WalkSites.CarsCom.ReadCardBadges(CarsComNoBadgeAndNoRating).ContainsKey(PostingAttributeNames.DealerRating));
    }

    [Fact]
    public void Autotrader_ReadsAGreatPrice()
    {
        AssertBadges(
            WalkSites.Autotrader.ReadCardBadges(AutotraderGreatPrice),
            (PostingAttributeNames.Deal, "Great Price"));
    }

    [Fact]
    public void Autotrader_ReadsAGoodPriceAPriceDropAndOnlinePaperwork()
    {
        AssertBadges(
            WalkSites.Autotrader.ReadCardBadges(AutotraderGoodPriceOnlinePaperworkPriceDrop),
            (PostingAttributeNames.Deal, "Good Price"),
            (PostingAttributeNames.PriceDrop, "Price Drop"),
            (PostingAttributeNames.Paperwork, "Online Paperwork"));
    }

    [Fact]
    public void Autotrader_LeavesOutABadgeTheTaskDoesNotRecord()
    {
        AssertBadges(
            WalkSites.Autotrader.ReadCardBadges(AutotraderHighDemandCard),
            (PostingAttributeNames.Deal, "Great Price"));
    }

    [Fact]
    public void Autotrader_ACardWithNoBadgeRecordsNothing()
    {
        Assert.Empty(WalkSites.Autotrader.ReadCardBadges(AutotraderNoBadge));
    }

    [Fact]
    public void Carvana_ReadsFreeShipping()
    {
        AssertBadges(
            WalkSites.Carvana.ReadCardBadges(CarvanaFreeShipping),
            (PostingAttributeNames.Shipping, "Free shipping"));
    }

    [Fact]
    public void Carvana_ReadsAPriceDropAndDoesNotTakeAShippingFeeForFreeShipping()
    {
        AssertBadges(
            WalkSites.Carvana.ReadCardBadges(CarvanaPriceDropWithShippingFee),
            (PostingAttributeNames.PriceDrop, "Price Drop"));
    }

    [Fact]
    public void Carvana_ReadsAGreatDealBesideAPriceDrop()
    {
        AssertBadges(
            WalkSites.Carvana.ReadCardBadges(CarvanaPriceDropGreatDeal),
            (PostingAttributeNames.Deal, "Great Deal"),
            (PostingAttributeNames.PriceDrop, "Price Drop"));
    }

    [Fact]
    public void Carvana_TheRecentSortLabelIsNotABadge()
    {
        Assert.Empty(WalkSites.Carvana.ReadCardBadges(CarvanaRecentTag));
    }

    [Theory]
    [InlineData("Shop All Price Drops")]
    [InlineData("Free Shipping\n(3)")]
    [InlineData("We've lowered prices to give you a Great Deal on every car")]
    [InlineData("")]
    public void Carvana_AFilterOrASentenceThatMentionsABadgeIsNotABadge(string text)
    {
        Assert.Empty(WalkSites.Carvana.ReadCardBadges(text));
    }

    [Fact]
    public void Badges_AreMatchedByWholeLineIgnoringSurroundingSpaceAndLineEndings()
    {
        AssertBadges(
            WalkSites.Carvana.ReadCardBadges("  Price Drop \r\nFree shipping\r\n"),
            (PostingAttributeNames.PriceDrop, "Price Drop"),
            (PostingAttributeNames.Shipping, "Free shipping"));
    }

    [Fact]
    public void ASiteWithNoBadgeReaderReadsNothing()
    {
        WalkSite withoutReader = WalkSites.Carvana with { CardBadgeReader = null };
        Assert.Empty(withoutReader.ReadCardBadges(CarvanaFreeShipping));
    }
}
