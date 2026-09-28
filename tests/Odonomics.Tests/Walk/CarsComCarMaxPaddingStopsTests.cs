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
/// CarMax delivery cards, two out-of-radius repeats, and (page 1 only) two empty-text links,
/// <c>02332385</c> and <c>016c322e</c>, that the real recording never resolves within this trimmed
/// fixture's own two pages: both are real in-radius, non-CarMax cars that only show their own
/// "(N mi)" card text on pages 3 and 6 of the full recording.
///
/// A no-distance card's own text never says whether it is in radius, and lessons 27401539, ffb24708,
/// 0cf0661c, 1cffc00e, 3f63ad16, and a7ab0dad all record real recordings where one never resolves at
/// all within the pages a search actually pages through. Marking the whole pair's coverage partial
/// whenever one is still unresolved when paging stops therefore fires on nearly every ordinary run for
/// some pairs, which freezes <c>RunSources.CoverageChain</c> at whatever run last happened not to hit
/// this shape and stops Gone detection from ever running again. So instead of a pair-level marker, a
/// no-distance card's own known posting (one the ledger already holds) is touched with a null price the
/// moment the card is reported, the same protective reason a below-floor or over-mileage card's known
/// posting is touched: its LastSeen moves with this run regardless of whether the card ever resolves, so
/// it is never read as having left the market just because this walk could not confirm it, and the
/// pair's own coverage never needs marking partial for the stop at all.</summary>
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

    /// <summary>A <c>touchKnownAsync</c> fake that treats <paramref name="knownUrls"/> as the ledger's
    /// own holdings, and records every touch it takes so a test can assert what price (or lack of one)
    /// and badges a no-distance card's own protective touch carried.</summary>
    private sealed class RecordingTouches(IReadOnlyCollection<string> knownUrls)
    {
        private readonly HashSet<string> _knownUrls = [.. knownUrls];

        public List<(string Url, decimal? Price, IReadOnlyDictionary<string, string> Badges)> Touches { get; } = [];

        public ValueTask<bool> TouchAsync(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken)
        {
            if (!_knownUrls.Contains(canonicalUrl))
            {
                return ValueTask.FromResult(false);
            }

            Touches.Add((canonicalUrl, cardPrice, cardBadges));
            return ValueTask.FromResult(true);
        }
    }

    [Fact]
    public async Task PriusSearch_StopsShortOfItsLastRealMatchButKeepsTheTwoUnresolvedKnownCardsCurrent()
    {
        // Page 1 of the real recording also carries two empty-text links, 02332385 and 016c322e, that
        // this fixture originally left out: neither is in that page's own unrendered.json (they did
        // render, just with no card text of their own), and both are real in-radius, non-CarMax cars
        // that only show their own "(N mi)" text on pages 3 and 6 of the full recording, well past
        // where this trimmed fixture's own page 2 stops. Marked known here, as they would be on a
        // repeat walk that had already saved them from an earlier run: this is the scenario the fix
        // protects, since paging stops before either card ever resolves its own distance.
        string[] unresolvedCanonicalUrls =
        [
            "https://www.cars.com/vehicledetail/02332385-6074-4e11-96c1-8860ef2b89e1/",
            "https://www.cars.com/vehicledetail/016c322e-a3ee-44ca-ba4a-8dd3600d310f/",
        ];
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-prius-carmax-padding-page-2-stops-here-cards.json"),
        });
        var touches = new RecordingTouches(unresolvedCanonicalUrls);
        int carMaxDealer = 0;
        int beyondRadius = 0;
        int noDistance = 0;
        var stoppedOnPaddingPages = new List<int>();

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=toyota-prius&zip=32833&maximum_distance=50",
            WalkPairSearches.UnboundedPool,
            touches.TouchAsync,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxDistanceMiles: 50,
            onBeyondRadius: () => beyondRadius++,
            onNoDistance: () => noDistance++,
            onCarMaxDealer: () => carMaxDealer++,
            onStoppedOnPaddingOnlyPage: page => stoppedOnPaddingPages.Add(page));

        // Page 1 held five in-radius, non-CarMax cars (this fixture's last new ones) plus the two
        // no-distance cards above, so paging continued to page 2. Page 2 held only two new CarMax
        // delivery cards and two cars already reported beyond radius on page 1, so it added nothing new
        // and paging stopped there: a third page was never requested, even though the two no-distance
        // cards from page 1 remain unresolved.
        Assert.Equal([1, 2], browser.Loads);
        Assert.Equal(5, pool.Count);
        string[] organicVins = ["c43f28e8", "c820bc6f", "ece1adc9", "4e057e41", "e551849a"];
        Assert.All(organicVins, vin => Assert.Contains(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        string[] carMaxVins = ["f09c5f97", "3bc5017d", "d758dc49", "16476643"];
        Assert.All(carMaxVins, vin => Assert.DoesNotContain(pool, href => href.Contains(vin, StringComparison.Ordinal)));

        Assert.Equal(4, carMaxDealer);
        Assert.Equal(2, beyondRadius);
        Assert.Equal(2, noDistance);

        // The stop happened on page 2 specifically because it held new (if excluded) content, not
        // because it was literally empty. Neither the CarMax nor the beyond-radius cards there would
        // ever have been kept regardless of what a later page held, so the stop is still reported (for
        // the console line), but nothing about it needs marking the pair's own coverage partial: each of
        // the two no-distance cards from page 1 was already touched, with no price of its own (their
        // wrapper text is never trusted), the moment it was first reported, so neither one is left to
        // freeze the pair's coverage the way a pair-level marker would.
        Assert.Equal([2], stoppedOnPaddingPages);
        Assert.Equal(unresolvedCanonicalUrls.OrderBy(u => u, StringComparer.Ordinal), touches.Touches.Select(t => t.Url).OrderBy(u => u, StringComparer.Ordinal));
        Assert.All(touches.Touches, t => Assert.Null(t.Price));
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
            onStoppedOnPaddingOnlyPage: page => stoppedOnPaddingPages.Add(page));

        // Page 2 here is the same as the "stops here" fixture above, plus one more card: 5313b206
        // (Parks Toyota of Deland, 38 mi, never seen on page 1), a genuine new in-radius, non-CarMax
        // match. That alone is enough to keep the paging going: page 3, empty in this fixture, is
        // requested and ends it there the ordinary way, never marked as a padding-only stop.
        Assert.Equal([1, 2, 3], browser.Loads);
        Assert.Contains(pool, href => href.Contains("5313b206", StringComparison.Ordinal));
        Assert.Empty(stoppedOnPaddingPages);
    }

    [Fact]
    public async Task ANoDistanceCardFirstSeenOnTheStoppingPage_IsTouchedTheSameWayAsAnEarlierOneIs()
    {
        // Page 1 here is trimmed further than the shared fixture: it drops the two no-distance links
        // (02332385, 016c322e) the other tests in this file pin, so this test is left with exactly one
        // no-distance card in the whole search, new only on page 2, the very page paging stops on. That
        // is the shape a padded carousel module can hand cars.com on the one page where a search's real
        // results happen to run out (the recorded base-model Camry Hybrid search's own last page does
        // this on every run, see lesson 0cf0661c). A no-distance card's own known posting is touched the
        // moment it is reported regardless of which page first reports it, so this one is protected
        // exactly the same way an earlier-page one is in the test above: no special-casing is needed for
        // when it was first seen, since nothing here depends any more on marking the whole pair partial.
        const string ambiguousCanonicalUrl = "https://www.cars.com/vehicledetail/ambiguous01/";
        List<PageLink> page1 = LoadCardsJson("cars-com-prius-carmax-padding-page-1-cards.json");
        page1.RemoveAll(c => c.Href.Contains("02332385", StringComparison.Ordinal) || c.Href.Contains("016c322e", StringComparison.Ordinal));
        List<PageLink> page2 = LoadCardsJson("cars-com-prius-carmax-padding-page-2-stops-here-cards.json");
        page2.RemoveAll(c => c.Href.Contains("16476643", StringComparison.Ordinal));
        page2.Add(new PageLink(
            $"{ambiguousCanonicalUrl}?sid=x",
            "",
            "$30,998\n\n22,022 mi.\nEst. $563/mo\nUsed 2025 Toyota Prius LE\n\nSuncoast Toyota\n\n4.6\nTampa, FL (95 mi)\nCheck Availability\n\n$28,998\n\n18,022 mi.\nEst. $511/mo\nUsed 2024 Toyota Prius LE\n\nGulf Coast Toyota\n\n4.2\nSarasota, FL (105 mi)\nCheck Availability"));
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = page1,
            [2] = page2,
        });
        var touches = new RecordingTouches([ambiguousCanonicalUrl]);
        var stoppedOnPaddingPages = new List<int>();
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            "https://www.cars.com/shopping/results/?models[]=toyota-prius&zip=32833&maximum_distance=50",
            WalkPairSearches.UnboundedPool,
            touches.TouchAsync,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            maxDistanceMiles: 50,
            onNoDistance: () => noDistance++,
            onStoppedOnPaddingOnlyPage: page => stoppedOnPaddingPages.Add(page));

        Assert.Equal([1, 2], browser.Loads);
        Assert.Equal(1, noDistance);
        Assert.Equal([2], stoppedOnPaddingPages);
        Assert.Equal([ambiguousCanonicalUrl], touches.Touches.Select(t => t.Url));
        Assert.Null(touches.Touches[0].Price);
    }
}
