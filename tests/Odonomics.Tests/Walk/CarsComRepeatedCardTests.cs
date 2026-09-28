using System.Text.Json;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves a cars.com card that repeats, canonical URL for canonical URL, across a search's own
/// later pages (walk run 20260928-184522, Honda Insight near Orlando: page 1 ends with Beaver Toyota of
/// St. Augustine, Buick Lakeland, and Westshore Honda; page 2 opens with two new cars and then repeats
/// those same three verbatim, only their tracking <c>sid=</c> query differing; every later page through
/// page 11 keeps repeating them) is recognized by <see cref="WalkSites.CanonicalDetailUrl"/> identity,
/// read once for the whole pair, and never counted a second time toward a later page's own added link
/// count, the count <see cref="WalkSearchPages.CollectLinksAsync"/>'s own "did this page add anything"
/// check uses to decide whether paging goes on. Page 11 itself (here stood in for by a third, shorter
/// page, the same collapsing <see cref="CarsComRadiusTests"/> already does for this same run) is nothing
/// but those same three repeats, so it adds nothing and paging ends there, the natural way a padded
/// cars.com search always ends (lesson 9dec3616): never revisited, never reported unrendered, and never a
/// reason to call the pair's coverage partial.</summary>
public class CarsComRepeatedCardTests
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

    [Fact]
    public async Task CollectLinksAsync_RepeatedCardsAcrossPages_ReadOnceAndNeverCountedAsALaterPagesYield()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-insight-repeated-cards-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-insight-repeated-cards-page-2-cards.json"),
            [3] = LoadCardsJson("cars-com-insight-repeated-cards-page-3-repeats-only-cards.json"),
        });
        var touched = new List<string>();
        int carMaxDealer = 0;

        ValueTask<bool> TrackTouches(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken ct)
        {
            touched.Add(canonicalUrl);
            return ValueTask.FromResult(false);
        }

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=honda-insight",
            WalkPairSearches.UnboundedPool,
            TrackTouches,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            onCarMaxDealer: () => carMaxDealer++);

        // Page 3 (standing in for the real run's page 11) is nothing but the same three repeats, so it
        // adds nothing new and paging stops there; a fourth page is never requested.
        Assert.Equal([1, 2, 3], browser.Loads);

        // Five organic cards: page 1's Driver's Mart Sanford and Toyota of Melbourne, plus the three that
        // go on to repeat (Beaver Toyota, Buick Lakeland, Westshore Honda), and page 2's own new Ogden
        // Motors. Two CarMax delivery cards on page 1 and one on page 2 are excluded as CarMax rather than
        // pooled, so they never appear here at all.
        Assert.Equal(6, pool.Count);
        Assert.Equal(pool.Distinct(), pool);
        string[] organicVins = ["af6fb68e", "80a6e699", "e42ccdc2", "a1a749e5", "7c3ad279", "49693b71"];
        Assert.All(organicVins, vin => Assert.Contains(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        string[] carMaxVins = ["9ae48e6a", "5616062b", "1200a372"];
        Assert.All(carMaxVins, vin => Assert.DoesNotContain(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        Assert.Equal(3, carMaxDealer);

        // Each of the three repeated cars is touched exactly once, off the first page it appeared on: the
        // second and third sightings, on page 2 and again on page 3, never call touchKnownAsync a second
        // time, so a card the ledger already knows never has its LastSeen bumped more than once and a new
        // one is never pooled twice under two different sid= query strings.
        string[] repeatedVins = ["e42ccdc2", "a1a749e5", "7c3ad279"];
        Assert.All(repeatedVins, vin => Assert.Single(touched, url => url.Contains(vin, StringComparison.Ordinal)));
        Assert.Equal(6, touched.Count);
    }
}
