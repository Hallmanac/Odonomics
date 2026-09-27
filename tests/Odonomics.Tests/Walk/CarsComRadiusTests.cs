using System.Text.Json;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves cars.com's link collection keeps a scenario's radius against every card's own stated
/// distance, over real recorded pages: walk run 20260927-113258, whose Honda Insight search near 32833
/// rendered pages 2 through 11 as nothing but four 65-to-96-mile Florida cards and two empty-text
/// nationwide recommendation links, so the pre-fix walk saved seventeen Insights from all over the
/// country before the unbounded pool finally read all eleven pages; and walk run 20260926-121057, whose
/// page 1 mixed three in-radius cards (two of them CarMax's own "delivery to &lt;city&gt; (N mi)" form) in
/// with the same four out-of-radius Florida cards.</summary>
public class CarsComRadiusTests
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

    /// <summary>Splits the recorded page-1 body text of run 20260926-121057 into its seven result cards,
    /// each ending at the "Check Availability" or "Certified Pre-Owned" line every card on that page has.</summary>
    private static List<string> LoadRecordedCardBlocks(string fileName)
    {
        string text = File.ReadAllText(FixturePath(fileName));
        string afterHeader = text[(text.IndexOf("Skip to Filters", StringComparison.Ordinal) + "Skip to Filters".Length)..];
        string section = afterHeader[..afterHeader.IndexOf("\nShop\n", StringComparison.Ordinal)].Trim('\n');

        List<string> blocks = [];
        List<string> current = [];
        foreach (string line in section.Split('\n'))
        {
            current.Add(line);
            if (line.Trim() is "Check Availability" or "Certified Pre-Owned")
            {
                blocks.Add(string.Join('\n', current).Trim('\n'));
                current = [];
            }
        }

        return blocks;
    }

    private sealed class FakeBrowser(Dictionary<int, List<PageLink>> pages)
    {
        public List<int> Loads { get; } = [];

        public Task<SearchPageContent> LoadAsync(string url, int pageNumber, CancellationToken _)
        {
            Loads.Add(pageNumber);
            IReadOnlyList<PageLink> anchors = pages.TryGetValue(pageNumber, out List<PageLink>? served) ? served : [];
            return Task.FromResult(new SearchPageContent(anchors));
        }
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    [Fact]
    public async Task RecordedPaddedPages_AddNoLinkAndStopPagingAfterTheFirstOfThem()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = LoadCardsJson("cars-com-insight-radius-search-page-1-cards.json"),
            [2] = LoadCardsJson("cars-com-insight-radius-search-page-2-cards.json"),
            [3] = LoadCardsJson("cars-com-insight-radius-search-page-3-cards.json"),
        });
        int beyondRadius = 0;
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom,
            search,
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            browser.LoadAsync,
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None,
            revisit: false,
            maxDistanceMiles: 50,
            onBeyondRadius: () => beyondRadius++,
            onNoDistance: () => noDistance++);

        // Page 2 held nothing but the same four out-of-radius Florida cards page 1 already carried plus
        // two fresh, empty-text nationwide links, so it added no new in-radius link and paging stopped
        // there: page 3 was never requested.
        Assert.Equal([1, 2], browser.Loads);

        // None of the out-of-radius Florida hrefs, nor either page's nationwide links, ever entered the pool.
        string[] beyondRadiusVins = ["e42ccdc2-fd2d-4265-80a5-9c13488f83bb", "8ce9a8c9-8b86-4ca2-91fd-4ea639508de7", "a1a749e5-d29e-44cb-a4db-88a3b9770059", "7c3ad279-4633-4052-8809-63a67c278e9d"];
        Assert.All(beyondRadiusVins, vin => Assert.DoesNotContain(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        string[] nationwideHrefs = ["49693b71-df19-4092-9e3f-81d408e93508", "1200a372-b8c9-4b72-b66e-4ae9c1d5b5eb"];
        Assert.All(nationwideHrefs, id => Assert.DoesNotContain(pool, href => href.Contains(id, StringComparison.Ordinal)));

        // Page 1 contributes its four out-of-radius Florida cards, and page 2 contributes two more distinct
        // ones (the site's own bounding heuristic hands its two nationwide links the wrapping container's
        // text, which still starts with an out-of-radius neighbor's distance): six distinct cars in total,
        // not sixty, since the four Florida cards repeating across pages are counted once each rather than
        // once per page they reappear on. Nothing was left for the "no distance" count here; that path is
        // covered directly against a genuinely empty card in WalkSitesTests.
        Assert.Equal(6, beyondRadius);
        Assert.Equal(0, noDistance);
    }

    [Fact]
    public void RecordedPageOne_20260926Run_SkipsTheStAugustineLakelandAndTampaCardsButKeepsTheInRadiusOnes()
    {
        List<string> cardBlocks = LoadRecordedCardBlocks("cars-com-insight-20260926-search.txt");
        Assert.Equal(7, cardBlocks.Count);

        List<PageLink> links =
        [
            .. cardBlocks.Select((card, i) => new PageLink($"https://www.cars.com/vehicledetail/card-{i}/?sid=x", "Used 2020 Honda Insight EX", card))
        ];
        List<PageLink> beyondRadius = [];
        List<PageLink> noDistance = [];

        IReadOnlyList<PageLink> kept = WalkSites.CarsCom.CollectDetailCards(
            links,
            poolSize: 10,
            maxDistanceMiles: 50,
            onBeyondRadius: beyondRadius.Add,
            onNoDistance: noDistance.Add);

        Assert.Empty(noDistance);
        Assert.Equal(4, beyondRadius.Count);
        Assert.All(beyondRadius, card => Assert.True(
            card.CardText.Contains("Augustine", StringComparison.Ordinal)
            || card.CardText.Contains("Lakeland", StringComparison.Ordinal)
            || card.CardText.Contains("Tampa", StringComparison.Ordinal)));

        Assert.Equal(3, kept.Count);
        Assert.Contains(kept, c => c.CardText.Contains("Sanford, FL (28 mi)", StringComparison.Ordinal));
        Assert.Contains(kept, c => c.CardText.Contains("$199 delivery to Orlando, FL (14 mi)", StringComparison.Ordinal));
        Assert.Contains(kept, c => c.CardText.Contains("$249 delivery to Orlando, FL (14 mi)", StringComparison.Ordinal));
    }
}
