using System.Text.Json;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the walk drops a CarMax search card before it is ever a detail-visit candidate when
/// the card's own text already states a model year below the scenario's floor or a mileage over its cap,
/// since CarMax's search URL carries both facets but does not actually honor the year range: a gap found
/// from walk run 20260927-192443, whose Camry Hybrid search (year floor 2018 in the URL) pooled ten
/// 2014-2017 cards. The trimmed fixture is thirty cards cut from that run's own recorded
/// camry-hybrid/cards.json, in the page's own order, and holds exactly those ten below-floor cards among
/// twenty that state a year at or above the floor (including 2025 and 2026 cards titled just "Camry",
/// CarMax's post-cutover base-model cards, which the scenario's own Camry Hybrid hybrid-only-from-2025
/// rule still has to accept).</summary>
public class CarMaxYearMileageFilterTests
{
    private static ListingQuery CamryHybridQuery(int yearMin = 2018, int maxMileage = 100000) =>
        new("Toyota", "Camry Hybrid", yearMin, "32822", 50, maxMileage, HybridOnlyFromModelYear: 2025);

    private static string FixturePath(string name) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name);

    private sealed record CardJson(string Href, string Text, string Card);

    private static List<PageLink> LoadCardsJson(string fileName)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<CardJson> cards = JsonSerializer.Deserialize<List<CardJson>>(File.ReadAllText(FixturePath(fileName)), options)
            ?? throw new InvalidOperationException($"{fileName} held no cards");
        return [.. cards.Select(c => new PageLink(c.Href, c.Text, c.Card))];
    }

    private static readonly string[] BelowFloorHrefs =
    [
        "https://www.carmax.com/car/29030244",
        "https://www.carmax.com/car/70247101",
        "https://www.carmax.com/car/70121501",
        "https://www.carmax.com/car/28879131",
        "https://www.carmax.com/car/70268519",
        "https://www.carmax.com/car/70097411",
        "https://www.carmax.com/car/70180116",
        "https://www.carmax.com/car/70255023",
        "https://www.carmax.com/car/70165866",
        "https://www.carmax.com/car/70009831",
    ];

    [Fact]
    public void CollectDetailCards_RecordedCamryHybridSearch_SkipsTheTenCardsBelowTheYearFloor()
    {
        List<PageLink> cards = LoadCardsJson("carmax-camry-hybrid-below-floor-cards.json");
        Assert.Equal(30, cards.Count);

        ListingQuery query = CamryHybridQuery();
        List<PageLink> belowFloor = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            cards,
            poolSize: 60,
            minYearFor: query.CardYearFloor,
            maxMileage: query.MaxMileage,
            onBelowYearFloor: belowFloor.Add);

        string[] keptHrefs = [.. kept.Select(c => c.Href)];
        Assert.Equal(20, kept.Count);
        Assert.Equal(10, belowFloor.Count);
        Assert.Equal(BelowFloorHrefs.OrderBy(h => h), belowFloor.Select(c => c.Href).OrderBy(h => h));
        Assert.All(BelowFloorHrefs, href => Assert.DoesNotContain(href, keptHrefs));

        // The post-cutover base-model cards ("2025 Toyota Camry", "2026 Toyota Camry", no "Hybrid" in the
        // title) are kept: their own year is at or above the hybrid-only-from-2025 cutover, so the raised
        // floor for a base-model card still passes them.
        Assert.Contains("https://www.carmax.com/car/29150918", keptHrefs);
        Assert.Contains("https://www.carmax.com/car/29194567", keptHrefs);
    }

    [Fact]
    public void CollectDetailCards_NoQueryFacetsGiven_KeepsEveryCardAsToday()
    {
        List<PageLink> cards = LoadCardsJson("carmax-camry-hybrid-below-floor-cards.json");

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(cards, poolSize: 60);

        Assert.Equal(cards.Count, kept.Count);
    }

    [Fact]
    public void CollectDetailCards_BaseModelCardBelowTheHybridOnlyCutoverButAboveTheScenarioFloor_IsSkipped()
    {
        // A synthetic card, since the real run never carried one: a base-model "Camry" title (no
        // "Hybrid") at 2020, above the scenario's own 2018 floor but below the 2025 hybrid-only cutover.
        // Such a car can never be a real Camry Hybrid candidate, so it is skipped the same as a card
        // below the plain floor rather than kept only to fail as "wrong model" on its detail page.
        var card = new PageLink("https://www.carmax.com/car/1", "", "View more\nCompare\n2020 Toyota Camry\nSE\n·\n40K mi\n$49 shipping\n$21,998");
        ListingQuery query = CamryHybridQuery();
        List<PageLink> belowFloor = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card],
            poolSize: 60,
            minYearFor: query.CardYearFloor,
            maxMileage: query.MaxMileage,
            onBelowYearFloor: belowFloor.Add);

        Assert.Empty(kept);
        Assert.Single(belowFloor);
    }

    [Fact]
    public void CollectDetailCards_CardOverTheMileageCap_IsSkippedAndNeverPooled()
    {
        var card = new PageLink("https://www.carmax.com/car/2", "", "View more\nCompare\n2022 Toyota Camry Hybrid\nLE\n·\n120K mi\n$49 shipping\n$21,998");
        ListingQuery query = CamryHybridQuery(maxMileage: 100000);
        List<PageLink> overMileage = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card],
            poolSize: 60,
            minYearFor: query.CardYearFloor,
            maxMileage: query.MaxMileage,
            onOverMileageCap: overMileage.Add);

        Assert.Empty(kept);
        Assert.Single(overMileage);
    }

    [Fact]
    public void CollectDetailCards_CardStatingNeitherYearNorMileage_IsKeptAsToday()
    {
        var card = new PageLink("https://www.carmax.com/car/3", "", "Save\n$21,998");
        ListingQuery query = CamryHybridQuery();

        IReadOnlyList<PageLink> kept = WalkSites.CarMax.CollectDetailCards(
            [card],
            poolSize: 60,
            minYearFor: query.CardYearFloor,
            maxMileage: query.MaxMileage,
            onBelowYearFloor: _ => throw new InvalidOperationException("should not be reported"),
            onOverMileageCap: _ => throw new InvalidOperationException("should not be reported"));

        Assert.Single(kept);
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    [Fact]
    public async Task CollectLinksAsync_BelowFloorCardsNeverEnterThePoolButAreStillCheckedAsKnown()
    {
        List<PageLink> cards = LoadCardsJson("carmax-camry-hybrid-below-floor-cards.json");
        ListingQuery query = CamryHybridQuery();
        int belowFloor = 0;
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
            minYearFor: query.CardYearFloor,
            maxMileage: query.MaxMileage,
            onBelowYearFloor: () => belowFloor++);

        Assert.Equal(10, belowFloor);
        Assert.Equal(20, pool.Count);
        Assert.All(BelowFloorHrefs, href => Assert.DoesNotContain(href, pool));

        // A below-floor card never earns a detail visit, but a known posting behind one is still touched
        // from its card, the same as a passing card's, so it is never later read as gone for want of a
        // visit this walk was never going to spend on it.
        Assert.All(BelowFloorHrefs, href => Assert.Contains(WalkSites.CanonicalDetailUrl(href), checkedAsKnownOrNew));
    }
}
