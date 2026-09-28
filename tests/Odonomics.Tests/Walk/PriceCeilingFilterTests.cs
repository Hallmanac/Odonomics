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

    [Fact]
    public void CollectDetailCards_CarsComCardWhoseDeliveryFeePushesItOverTheCeiling_IsSkippedAsCarMaxNotOverCeiling()
    {
        // $24,900 alone is under the $25,000 ceiling; the CarMax-brokered listing's $249 delivery fee
        // would push it to $25,149, but the card's own delivery fee line marks it as CarMax first (see
        // WalkSite.CollectDetailCards), checked ahead of the price ceiling, so it is reported under that
        // reason instead: CarMax's own walk already covers it nationwide regardless of what it costs.
        var card = new PageLink(
            "https://www.cars.com/vehicledetail/x/?sid=1",
            "Used 2020 Honda Insight EX",
            "$24,900\n80,924 mi.\nEst. $345/mo\nUsed 2020 Honda Insight EX\nCarMax Town Center\n$249 delivery to Orlando, FL (14 mi)");
        List<PageLink> overCeiling = [];
        List<PageLink> carMaxDealer = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarsCom.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: overCeiling.Add, onCarMaxDealer: carMaxDealer.Add);

        Assert.Empty(kept);
        Assert.Empty(overCeiling);
        Assert.Single(carMaxDealer);
    }

    private static PageLink CarvanaCard(string href, decimal price) =>
        new(href, "", $"2020 Honda Insight\nEX\n38k miles\nCurrent price:\n${price:N0}\n$430/mo\nestimated\n$0 cash down\nFree shipping\nGet it today");

    [Fact]
    public void CollectDetailCards_APriceCeilingSkipNeverLetsAPaddingCardBackfillTheStatedCount()
    {
        // The page states "16 cars" but renders 20: the first 16 are the exact matches, 10 of them over
        // the $25,000 ceiling, and the last 4 are padding (other models the search facets never asked
        // for). Before the fix, letting the price filter run over all 20 before the stated count was
        // taken meant the 4 padding cards backfilled the room the 10 skipped exact matches left, so they
        // too earned a detail visit; they must never be pooled or even reported over the ceiling.
        List<PageLink> exactMatches =
        [
            .. Enumerable.Range(0, 10).Select(i => CarvanaCard($"https://www.carvana.com/vehicle/over{i}", 30000m)),
            .. Enumerable.Range(0, 6).Select(i => CarvanaCard($"https://www.carvana.com/vehicle/under{i}", 20000m)),
        ];
        List<PageLink> padding = [.. Enumerable.Range(0, 4).Select(i => CarvanaCard($"https://www.carvana.com/vehicle/pad{i}", 20000m))];
        List<PageLink> cards = [.. exactMatches, .. padding];
        List<PageLink> overCeiling = [];

        IReadOnlyList<PageLink> kept = WalkSites.Carvana.CollectDetailCards(
            cards, poolSize: 200, searchPageText: "16 cars", maxPrice: 25000, onOverPriceCeiling: overCeiling.Add);

        Assert.Equal(6, kept.Count);
        Assert.Equal(10, overCeiling.Count);
        Assert.All(kept, card => Assert.DoesNotContain("pad", card.Href));
        Assert.All(overCeiling, card => Assert.DoesNotContain("pad", card.Href));
    }

    [Fact]
    public void CollectDetailCards_CarvanaCardWhoseShippingFeePushesItOverTheCeiling_IsSkipped()
    {
        // $24,990 alone is under the $25,000 ceiling; the card's own $690 shipping fee pushes it to $25,680.
        var card = new PageLink(
            "https://www.carvana.com/vehicle/1",
            "",
            "2020 Honda Insight\nEX\n38k miles\nCurrent price:\n$24,990\n$430/mo\nestimated\n$0 cash down\n$690 shipping\nGet it Monday");
        List<PageLink> overCeiling = [];

        IReadOnlyList<PageLink> kept = WalkSites.Carvana.CollectDetailCards(
            [card], poolSize: 60, maxPrice: 25000, onOverPriceCeiling: overCeiling.Add);

        Assert.Empty(kept);
        Assert.Single(overCeiling);
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, bool cardTextTrusted, CancellationToken cancellationToken) => ValueTask.FromResult(false);

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

        ValueTask<bool> TrackTouches(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, bool cardTextTrusted, CancellationToken ct)
        {
            checkedAsKnownOrNew.Add(canonicalUrl);
            return NoneKnown(canonicalUrl, cardPrice, cardBadges, cardTextTrusted, ct);
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
