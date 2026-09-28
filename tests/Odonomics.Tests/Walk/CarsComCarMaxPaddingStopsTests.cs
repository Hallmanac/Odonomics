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
        var stoppedOnPaddingPages = new List<int>();

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
            onStoppedOnPaddingOnlyPage: stoppedOnPaddingPages.Add);

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
        // because it was literally empty: this is what lets the caller mark the pair's coverage
        // partial rather than assume every in-radius car was actually read.
        Assert.Equal([2], stoppedOnPaddingPages);
    }

    [Fact]
    public async Task APageWithEvenOneNewInRadiusNonCarMaxCar_KeepsThePagingGoing()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-prius-carmax-padding-page-2-with-new-organic-car-cards.json"),
        });
        var stoppedOnPaddingPages = new List<int>();

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
            onStoppedOnPaddingOnlyPage: stoppedOnPaddingPages.Add);

        // Page 2 here is the same as the "stops here" fixture above, plus one more card: 5313b206
        // (Parks Toyota of Deland, 38 mi, never seen on page 1), a genuine new in-radius, non-CarMax
        // match. That alone is enough to keep the paging going: page 3, empty in this fixture, is
        // requested and ends it there the ordinary way, never marked as a padding-only stop.
        Assert.Equal([1, 2, 3], browser.Loads);
        Assert.Contains(pool, href => href.Contains("5313b206", StringComparison.Ordinal));
        Assert.Empty(stoppedOnPaddingPages);
    }
}
