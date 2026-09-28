using System.Text.Json;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the fix for the cars.com Prius walk that kept paging through CarMax's own
/// nationwide-inventory cards for dozens of pages (walk 20260928-184522, near 32833, radius 50 mi):
/// from page 3 on, every page held 13 cards, 10 of them the same out-of-radius cars, and 1 to 3 new
/// ones, almost always CarMax delivery cards the walk already excludes. Pages 1 and 2 of that same
/// recording already carry the same shape a page 3-and-on does, just with organic matches still mixed
/// in on page 1 (the fix's own stop rule cares about the last page with one of those, wherever it
/// falls): page 2 there is nothing but two new CarMax cards and cars already reported beyond radius on
/// page 1. Every fixture here is cut straight from that recording's own <c>prius/cards.json</c> and
/// <c>prius/cards-2.json</c>, trimmed to the cards that matter: five in-radius, non-CarMax dealers, two
/// CarMax delivery cards, and two out-of-radius repeats.</summary>
public class CarsComCarMaxPaddingStopsTests
{
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

    private sealed class FakeBrowser(Dictionary<int, List<PageLink>> pages)
    {
        public List<int> Loads { get; } = [];

        public Task<SearchPageContent> LoadAsync(string url, int pageNumber, CancellationToken _)
        {
            Loads.Add(pageNumber);
            IReadOnlyList<PageLink> links = pages.TryGetValue(pageNumber, out List<PageLink>? served) ? served : [];
            return Task.FromResult(new SearchPageContent(links));
        }
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    [Fact]
    public async Task PriusSearch_StopsAFewPagesPastItsLastNewInRadiusNonCarMaxCar()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-prius-carmax-padding-page-2-stops-here-cards.json"),
        });
        int carMaxDealer = 0;
        int beyondRadius = 0;
        var stoppedOnPaddingPages = new List<(int Page, bool HadAmbiguousCard)>();

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=toyota-prius&zip=32833&maximum_distance=50",
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxDistanceMiles: 50,
            onBeyondRadius: () => beyondRadius++,
            onCarMaxDealer: () => carMaxDealer++,
            onStoppedOnPaddingOnlyPage: (page, hadAmbiguousCard) => stoppedOnPaddingPages.Add((page, hadAmbiguousCard)));

        // Page 1 held five in-radius, non-CarMax cars (this fixture's last new ones), so paging
        // continued to page 2. Page 2 held only two new CarMax delivery cards and two cars already
        // reported beyond radius on page 1, so it added nothing new and paging stopped there, one page
        // past the last real match: a third page was never requested.
        Assert.Equal([1, 2], browser.Loads);
        Assert.Equal(5, pool.Count);
        string[] organicVins = ["c43f28e8", "c820bc6f", "ece1adc9", "4e057e41", "e551849a"];
        Assert.All(organicVins, vin => Assert.Contains(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        string[] carMaxVins = ["f09c5f97", "3bc5017d", "d758dc49", "16476643"];
        Assert.All(carMaxVins, vin => Assert.DoesNotContain(pool, href => href.Contains(vin, StringComparison.Ordinal)));

        Assert.Equal(4, carMaxDealer);
        Assert.Equal(2, beyondRadius);

        // The stop happened on page 2 specifically because it held new (if excluded) content, not
        // because it was literally empty. But that content was only CarMax and beyond-radius cards,
        // neither of which this walk would ever have kept regardless of what a later page held, so the
        // stop is reported as unambiguous: the caller is not meant to mark the pair's coverage partial
        // for it.
        Assert.Equal([(2, false)], stoppedOnPaddingPages);
    }

    [Fact]
    public async Task APageWithEvenOneNewInRadiusNonCarMaxCar_KeepsThePagingGoing()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-prius-carmax-padding-page-2-with-new-organic-car-cards.json"),
        });
        var stoppedOnPaddingPages = new List<(int Page, bool HadAmbiguousCard)>();

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=toyota-prius&zip=32833&maximum_distance=50",
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxDistanceMiles: 50,
            onStoppedOnPaddingOnlyPage: (page, hadAmbiguousCard) => stoppedOnPaddingPages.Add((page, hadAmbiguousCard)));

        // Page 2 here is the same as the "stops here" fixture above, plus one more card: 5313b206
        // (Parks Toyota of Deland, 38 mi, never seen on page 1), a genuine new in-radius, non-CarMax
        // match. That alone is enough to keep the paging going: page 3, empty in this fixture, is
        // requested and ends it there the ordinary way, never marked as a padding-only stop.
        Assert.Equal([1, 2, 3], browser.Loads);
        Assert.Contains(pool, href => href.Contains("5313b206", StringComparison.Ordinal));
        Assert.Empty(stoppedOnPaddingPages);
    }

    [Fact]
    public async Task APageWhoseOnlyNewCardStatesNoDistance_StopsPagingAndIsReportedAmbiguous()
    {
        // Same page 1 as the other cases here, but page 2 swaps in a wrapper-text card (empty href
        // text, several neighboring cards' distances bound into one "card" field, see
        // WalkSite.CollectDetailCards's own CardDistanceReader remarks) in place of one of the CarMax
        // cards: unlike a CarMax or beyond-radius card, this one's own text never says whether it is in
        // radius, so the stop it causes must be reported as ambiguous rather than safe to treat as full
        // coverage.
        var page2 = LoadCardsJson("cars-com-prius-carmax-padding-page-2-stops-here-cards.json");
        page2.RemoveAll(c => c.Href.Contains("16476643", StringComparison.Ordinal));
        page2.Add(new PageLink(
            "https://www.cars.com/vehicledetail/ambiguous01/?sid=x",
            "",
            "$30,998\n\n22,022 mi.\nEst. $563/mo\nUsed 2025 Toyota Prius LE\n\nSuncoast Toyota\n\n4.6\nTampa, FL (95 mi)\nCheck Availability\n\n$28,998\n\n18,022 mi.\nEst. $511/mo\nUsed 2024 Toyota Prius LE\n\nGulf Coast Toyota\n\n4.2\nSarasota, FL (105 mi)\nCheck Availability"));
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json"),
            [2] = page2,
        });
        var stoppedOnPaddingPages = new List<(int Page, bool HadAmbiguousCard)>();
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=toyota-prius&zip=32833&maximum_distance=50",
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxDistanceMiles: 50,
            onNoDistance: () => noDistance++,
            onStoppedOnPaddingOnlyPage: (page, hadAmbiguousCard) => stoppedOnPaddingPages.Add((page, hadAmbiguousCard)));

        Assert.Equal([1, 2], browser.Loads);
        Assert.Equal(1, noDistance);
        Assert.Equal([(2, true)], stoppedOnPaddingPages);
    }
}
