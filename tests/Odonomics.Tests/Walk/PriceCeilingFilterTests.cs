using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the walk drops a search card before it is ever a detail-visit candidate when the
/// card's own stated price, plus any shipping or delivery fee that same card states, already comes to
/// more than the scenario's price ceiling (<c>filters.maxPrice</c>): Brian's 2026-09-27 ruling that a car
/// he'd never buy should cost no page visits, on every site rather than only CarMax's own year-and-mileage
/// check (see <see cref="CarMaxYearMileageFilterTests"/>). CarMax's own card shape (title, mileage, a fee
/// line, then the price) is reused here since it already has both a <see cref="WalkSite.CardPriceReader"/>
/// and a <see cref="WalkSite.CardFeeReader"/>.</summary>
public class PriceCeilingFilterTests
{
    private static PageLink Card(int id, string cardText) => new($"https://www.carmax.com/car/{id}", "", cardText);

    [Fact]
    public void CollectDetailCards_PriceAloneOverTheCeilingWithNoShippingFeeStated_IsSkipped()
    {
        var card = Card(1, "View more\nCompare\n2025 Toyota Camry\nSE\n·\n14K mi\nAvailable today·Orlando\nEst. $506/mo\n·\n$30,998");
        List<PageLink> overCeiling = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: overCeiling.Add);

        Assert.Empty(kept);
        Assert.Single(overCeiling);
    }

    [Fact]
    public void CollectDetailCards_PriceUnderTheCeilingButShippingFeePushesItOver_IsSkipped()
    {
        // $24,998 alone is under the $25,000 ceiling; the $499 shipping fee pushes it to $25,497.
        var card = Card(2, "View more\nCompare\n2016 Toyota Camry Hybrid\nXLE\n·\n74K mi\n$499 shipping·Get it by Oct 4 - Oct 10\nEst. $311/mo\n·\n$24,998");
        List<PageLink> overCeiling = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: overCeiling.Add);

        Assert.Empty(kept);
        Assert.Single(overCeiling);
    }

    [Fact]
    public void CollectDetailCards_PricePlusShippingFeeAtOrUnderTheCeiling_IsKept()
    {
        // $24,998 + $2 shipping = $25,000 exactly, at the ceiling and not over it.
        var card = Card(3, "View more\nCompare\n2016 Toyota Camry Hybrid\nXLE\n·\n74K mi\n$2 shipping·Get it by Oct 4 - Oct 10\nEst. $311/mo\n·\n$24,998");

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: _ => throw new InvalidOperationException("should not be reported"));

        Assert.Single(kept);
    }

    [Fact]
    public void CollectDetailCards_CardStatingNoPrice_IsKeptRegardlessOfTheCeiling()
    {
        var card = Card(4, "Save\nAvailable today·Orlando");

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: _ => throw new InvalidOperationException("should not be reported"));

        Assert.Single(kept);
    }

    [Fact]
    public void CollectDetailCards_NoMaxPriceGiven_KeepsEveryCardAsToday()
    {
        var card = Card(5, "View more\nCompare\n2025 Toyota Camry\nSE\n·\n14K mi\nAvailable today·Orlando\nEst. $506/mo\n·\n$99,998");

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards([card], poolSize: 60);

        Assert.Single(kept);
    }

    [Fact]
    public void CollectDetailCards_SiteWhoseCardPriceAlreadyIncludesShipping_NeverAddsTheFeeTwice()
    {
        // CarGurus stores AskingPriceFromCard: the card's own price already has its shipping fee inside
        // it ("Price includes $462 shipping"), so adding CardFeeReader's fee again would double-count it
        // and wrongly skip a card that is actually within the ceiling.
        var card = new PageLink("https://www.cargurus.com/details/6", "", "Price includes $462 shipping\n$24,998");

        IReadOnlyList<PageLink> kept = WalkSites.CarGurus.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: _ => throw new InvalidOperationException("should not be reported"));

        Assert.Single(kept);
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    [Fact]
    public async Task CollectLinksAsync_OverCeilingCardsNeverEnterThePoolButAreStillCheckedAsKnown()
    {
        var overCeilingCard = Card(7, "View more\nCompare\n2025 Toyota Camry\nSE\n·\n14K mi\nAvailable today·Orlando\nEst. $506/mo\n·\n$30,998");
        var withinCeilingCard = Card(8, "View more\nCompare\n2020 Toyota Camry Hybrid\nLE\n·\n60K mi\n$49 shipping·Get it by Monday\nEst. $311/mo\n·\n$19,998");
        List<PageLink> cards = [overCeilingCard, withinCeilingCard];
        int overCeiling = 0;
        var checkedAsKnownOrNew = new List<string>();

        Task<SearchPageContent> LoadPageAsync(string url, int pageNumber, CancellationToken ct) =>
            Task.FromResult(new SearchPageContent(cards));

        ValueTask<bool> TrackTouches(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken ct)
        {
            checkedAsKnownOrNew.Add(canonicalUrl);
            return NoneKnown(canonicalUrl, cardPrice, cardBadges, ct);
        }

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarMax,
            "https://www.carmax.com/cars/toyota/camry/hybrid/2018-2027?zip=32822&distance=nationwide&mileage=100000",
            WalkPairSearches.UnboundedPool,
            TrackTouches,
            LoadPageAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxPrice: 25000,
            onOverPriceCeiling: () => overCeiling++);

        Assert.Equal(1, overCeiling);
        Assert.Equal([withinCeilingCard.Href], pool);

        // An over-ceiling card never earns a detail visit, but a known posting behind it is still touched
        // from its card, the same as a below-floor or over-mileage one, so it is never later read as gone
        // for want of a visit this walk was never going to spend on it.
        Assert.Contains(WalkSites.CanonicalDetailUrl(overCeilingCard.Href), checkedAsKnownOrNew);
        Assert.Contains(WalkSites.CanonicalDetailUrl(withinCeilingCard.Href), checkedAsKnownOrNew);
    }
}
