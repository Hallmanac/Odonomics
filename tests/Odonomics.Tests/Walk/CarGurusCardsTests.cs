using System.Text.Json;
using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves what a CarGurus search card yields: its price, its fee posture, the shipping it says is in the price,
/// and its deal badge. The card texts are the ones recorded on 2026-09-26 (a Honda Insight search near 32833, 2019 and
/// newer, under 100,000 miles), read by the same nearest-ancestor-with-a-price pull the walk uses.</summary>
public class CarGurusCardsTests
{
    private sealed record CardEntry(string Href, string Text, string Card);

    private static string CardOf(string listingId, string fixture = "cargurus-insight-search-cards.json")
    {
        string json = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", fixture));
        return (JsonSerializer.Deserialize<List<CardEntry>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [])
            .First(c => c.Href.Contains($"/details/{listingId}?") && c.Href.Contains("sponsoredType=NONE"))
            .Card;
    }

    // 457043244: home delivery, $462 shipping in the price; "Price includes fees"; Fair Deal.
    // 459400072: store transfer, $699 shipping in the price; "No additional dealer fees"; Fair Deal.
    // 458081858: home delivery, "Free home delivery"; Great Deal.
    // 458757260: at a nearby dealer (no delivery line); "Price includes fees"; Good Deal.
    // 459797578: store transfer; "Uncertain" in place of a badge.
    // 459198997: "High Priced". 458373266: "Overpriced".

    [Theory]
    [InlineData("457043244", 21241)]
    [InlineData("459400072", 27697)]
    [InlineData("458081858", 18799)]
    [InlineData("458757260", 19394)]
    public void CarGurusPrice_RecordedCard_IsTheDeliveredPriceOnALineOfItsOwn(string listingId, int expected)
    {
        Assert.Equal(expected, CardPrices.CarGurusPrice(CardOf(listingId)));
    }

    [Theory]
    [InlineData("456971502", 22673)]
    [InlineData("458613016", 24120)]
    [InlineData("456344008", 24118)]
    public void CarGurusPrice_RecordedPriceDropCard_IsTheCurrentPriceAfterTheOldOne(string listingId, int expected)
    {
        Assert.Equal(expected, CardPrices.CarGurusPrice(CardOf(listingId, "cargurus-corolla-hybrid-search-cards.json")));
    }

    [Fact]
    public void CarGurusPrice_SkipsAPriceDropAmountTheOldPriceAndTheMonthlyEstimate()
    {
        const string card = "Price drop\n-$539\nSave this listing\n2024 Toyota Corolla Hybrid\nSE FWD\n35,269 mi\nGreat Deal\n$23,212\n$22,673\nPrice includes fees\n$424/mo est.\nCheck availability";

        Assert.Equal(22673, CardPrices.CarGurusPrice(card));
    }

    [Theory]
    [InlineData("2022 Honda Insight\n$373/mo est.")]
    [InlineData("Price includes $462 shipping")]
    [InlineData("")]
    public void CarGurusPrice_CardWithNoPriceLine_ReturnsNull(string card)
    {
        Assert.Null(CardPrices.CarGurusPrice(card));
    }

    [Theory]
    [InlineData("457043244", 462)]
    [InlineData("459400072", 699)]
    [InlineData("459797578", 1899)]
    public void ReadFee_ShippingCard_ReturnsTheAmountInThePrice(string listingId, int expected)
    {
        Assert.Equal(new CardFee(expected), CarGurusCards.ReadFee(CardOf(listingId)));
    }

    [Fact]
    public void ReadFee_FreeHomeDelivery_ReturnsZero()
    {
        Assert.Equal(new CardFee(0m), CarGurusCards.ReadFee(CardOf("458081858")));
    }

    [Fact]
    public void ReadFee_CardWithNoDeliveryLine_ReturnsNullRatherThanFree()
    {
        Assert.Null(CarGurusCards.ReadFee(CardOf("458757260")));
    }

    [Theory]
    [InlineData("457043244")]
    [InlineData("458757260")]
    [InlineData("458081858")]
    public void ReadCarGurusCard_PriceIncludesFees_IsAllInWithNoFeeLines(string listingId)
    {
        FeeStatement statement = FeeStatements.ReadCarGurusCard(CardOf(listingId));

        Assert.Equal(new FeeStatement(FeePostures.AllIn, null), statement);
    }

    [Fact]
    public void ReadCarGurusCard_NoAdditionalDealerFees_IsAllIn()
    {
        Assert.Equal(FeePostures.AllIn, FeeStatements.ReadCarGurusCard(CardOf("459400072")).Posture);
    }

    [Fact]
    public void ReadCarGurusCard_ShippingLineAlone_IsAllInSinceThePriceIncludesIt()
    {
        Assert.Equal(FeePostures.AllIn, FeeStatements.ReadCarGurusCard("Home delivery from Delray Beach, FL\nPrice includes $462 shipping\nFair Deal\n$21,241").Posture);
    }

    [Theory]
    [InlineData("Fair Deal\n$21,241\nCheck availability")]
    [InlineData("Price includes fees and taxes\n$21,241")]
    [InlineData("Free home delivery\n$18,799")]
    [InlineData("")]
    public void ReadCarGurusCard_NoFeeLine_IsUnknownRatherThanAllIn(string card)
    {
        Assert.Equal(new FeeStatement(FeePostures.Unknown, null), FeeStatements.ReadCarGurusCard(card));
    }

    [Theory]
    [InlineData("458081858", "Great Deal")]
    [InlineData("458757260", "Good Deal")]
    [InlineData("457043244", "Fair Deal")]
    [InlineData("459198997", "High Priced")]
    [InlineData("458373266", "Overpriced")]
    public void CarGurusBadges_RecordedCard_KeepsTheDealBadgeInTheSitesOwnWords(string listingId, string expected)
    {
        IReadOnlyDictionary<string, string> badges = CardBadges.CarGurus(CardOf(listingId));

        Assert.Equal(new Dictionary<string, string> { [PostingAttributeNames.Deal] = expected }, badges);
    }

    [Fact]
    public void CarGurusBadges_UncertainCard_ReadsNoBadge()
    {
        Assert.Empty(CardBadges.CarGurus(CardOf("459797578")));
    }

    [Fact]
    public void CarGurusBadges_ASentenceThatMentionsADeal_IsNotABadge()
    {
        Assert.Empty(CardBadges.CarGurus("Find a Great Deal near you\nSee Good Deal listings"));
    }
}
