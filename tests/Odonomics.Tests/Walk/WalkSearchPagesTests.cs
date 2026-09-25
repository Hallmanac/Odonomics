using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves a paged site's search is followed page by page into one candidate pool, and that a
/// site with no paging is loaded once. The "browser" is a fake that serves canned anchors by page
/// number, so nothing here opens a page.</summary>
public class WalkSearchPagesTests
{
    private const string CarvanaSearch = "https://www.carvana.com/cars/filters?zip=32114&cvnaid=abc";

    private static List<PageLink> CarvanaCards(string prefix, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PageLink($"https://www.carvana.com/vehicle/{prefix}{i}", ""))];

    private sealed class FakeBrowser(Dictionary<int, List<PageLink>> pages)
    {
        public List<(string Url, int PageNumber)> Loads { get; } = [];

        public Task<IReadOnlyList<PageLink>> LoadAsync(string url, int pageNumber, CancellationToken _)
        {
            Loads.Add((url, pageNumber));
            IReadOnlyList<PageLink> anchors = pages.TryGetValue(pageNumber, out List<PageLink>? served) ? served : [];
            return Task.FromResult(anchors);
        }
    }

    [Fact]
    public async Task Carvana_FollowsPagesUntilAPageAddsNothingNew()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 23),
            [3] = CarvanaCards("c", 9),
            [4] = CarvanaCards("c", 9),
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, browser.LoadAsync, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(53, pool.Count);
        Assert.Contains("https://www.carvana.com/vehicle/a0", pool);
        Assert.Contains("https://www.carvana.com/vehicle/b22", pool);
        Assert.Contains("https://www.carvana.com/vehicle/c8", pool);
    }

    [Fact]
    public async Task Carvana_RequestsTheSameUrlWithPageNAppended()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 5),
            [2] = CarvanaCards("b", 5),
        });

        await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, browser.LoadAsync, CancellationToken.None);

        Assert.Equal(
            [CarvanaSearch, $"{CarvanaSearch}&page=2", $"{CarvanaSearch}&page=3"],
            browser.Loads.Select(l => l.Url));
    }

    [Fact]
    public async Task Carvana_StopsPagingOnceThePoolIsFullAndTruncatesToIt()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 23),
            [3] = CarvanaCards("c", 23),
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 30, browser.LoadAsync, CancellationToken.None);

        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(30, pool.Count);
        Assert.Equal("https://www.carvana.com/vehicle/b8", pool[^1]);
    }

    [Fact]
    public async Task Carvana_AFirstPageThatFillsThePoolIsNotFollowedByAnother()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 21), [2] = CarvanaCards("b", 21) });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 10, browser.LoadAsync, CancellationToken.None);

        Assert.Single(browser.Loads);
        Assert.Equal(10, pool.Count);
    }

    [Fact]
    public async Task Carvana_FoldsALinkThatRepeatsUnderADifferentQueryStringAcrossPages()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [new PageLink("https://www.carvana.com/vehicle/1?x=1", "")],
            [2] = [new PageLink("https://www.carvana.com/vehicle/1?x=2", "")],
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, browser.LoadAsync, CancellationToken.None);

        Assert.Equal(["https://www.carvana.com/vehicle/1?x=1"], pool);
        Assert.Equal(2, browser.Loads.Count);
    }

    [Fact]
    public async Task Carvana_AnEmptyFirstPageEndsTheSearchAfterOnePage()
    {
        var browser = new FakeBrowser([]);

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 60, browser.LoadAsync, CancellationToken.None);

        Assert.Empty(pool);
        Assert.Single(browser.Loads);
    }

    [Fact]
    public async Task CarsCom_IsLoadedOnceEvenWhenThePoolIsNotFull()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [.. Enumerable.Range(0, 5).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{i}/?sid=x", ""))],
            [2] = [new PageLink("https://www.cars.com/vehicledetail/99/?sid=x", "")],
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 60, browser.LoadAsync, CancellationToken.None);

        Assert.Equal([(search, 1)], browser.Loads);
        Assert.Equal(5, pool.Count);
    }

    [Fact]
    public async Task PagedSearchFeedsThePairWalkAsOneSearchWithItsPagesRecordedInOrder()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 3),
            [2] = CarvanaCards("b", 3),
            [3] = CarvanaCards("c", 3),
        });
        List<string> visited = [];

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.Carvana,
            [CarvanaSearch],
            4,
            (url, _, poolSize, ct) => WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, url, poolSize, browser.LoadAsync, ct),
            (link, _, _) =>
            {
                visited.Add(link);
                return Task.FromResult(DetailPageOutcome.Upserted);
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(4, tally.Visited);
        Assert.Equal(4, visited.Count);
        Assert.Contains("https://www.carvana.com/vehicle/b0", visited);
        Assert.Equal([1, 2, 3], browser.Loads.Select(l => l.PageNumber));
    }
}
