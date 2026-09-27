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

        // None of the out-of-radius Florida hrefs, nor any page's empty-anchor or nationwide links, ever
        // entered the pool. Page 1 carries three empty-anchor links (af6fb68e, 80a6e699, and the
        // attribution_type=ship nationwide link 9ae48e6a) whose CardText is the whole five-card wrapper
        // blob: its first stated distance, 14 mi, belongs to the wrapper's leading CarMax card, not to any
        // of these three, so a reader that measured them by that first distance would wrongly pool them
        // (lesson 9514dea8). Page 2's two nationwide links, 49693b71 and 1200a372, carry a four-card
        // wrapper of their own.
        string[] beyondRadiusVins = ["e42ccdc2-fd2d-4265-80a5-9c13488f83bb", "8ce9a8c9-8b86-4ca2-91fd-4ea639508de7", "a1a749e5-d29e-44cb-a4db-88a3b9770059", "7c3ad279-4633-4052-8809-63a67c278e9d"];
        Assert.All(beyondRadiusVins, vin => Assert.DoesNotContain(pool, href => href.Contains(vin, StringComparison.Ordinal)));
        string[] multiCardWrapperHrefs = ["af6fb68e-25ea-4c9f-9459-4f6f20d97f1d", "80a6e699-f24c-40fc-9075-235a6d8133e1", "9ae48e6a-57ae-4da4-909b-11f95a171140", "49693b71-df19-4092-9e3f-81d408e93508", "1200a372-b8c9-4b72-b66e-4ae9c1d5b5eb"];
        Assert.All(multiCardWrapperHrefs, id => Assert.DoesNotContain(pool, href => href.Contains(id, StringComparison.Ordinal)));

        // The only in-radius card on either page is page 1's own-text 5616062b (14 mi): the pool holds it
        // and nothing else.
        Assert.Single(pool);
        Assert.Contains(pool, href => href.Contains("5616062b-4439-4e07-badb-8240b99bcd83", StringComparison.Ordinal));

        // Page 1 contributes its four out-of-radius Florida cards under "beyond radius" and its three
        // multi-card-wrapper links (a card text naming more than one distance states none) under "no
        // distance stated". Page 2 repeats the same four Florida cards, already reported once each from
        // page 1, and adds its own two wrapper links as two more "no distance stated" reports.
        Assert.Equal(4, beyondRadius);
        Assert.Equal(5, noDistance);
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
