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

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private sealed class FakeBrowser(Dictionary<int, List<PageLink>> pages, Dictionary<int, string>? texts = null)
    {
        public List<(string Url, int PageNumber)> Loads { get; } = [];

        public Task<SearchPageContent> LoadAsync(string url, int pageNumber, CancellationToken _)
        {
            Loads.Add((url, pageNumber));
            IReadOnlyList<PageLink> anchors = pages.TryGetValue(pageNumber, out List<PageLink>? served) ? served : [];
            string? text = texts is not null && texts.TryGetValue(pageNumber, out string? servedText) ? servedText : null;
            return Task.FromResult(new SearchPageContent(anchors, text));
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

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

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

        await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

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

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 30, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(30, pool.Count);
        Assert.Equal("https://www.carvana.com/vehicle/b8", pool[^1]);
    }

    [Fact]
    public async Task Carvana_APoolThatFillsWithPagesStillToRead_ReportsTheCollectionAsCapped()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 23),
            [3] = CarvanaCards("c", 23),
        });
        int capped = 0;

        await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 30, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None);

        Assert.Equal(1, capped);
    }

    [Fact]
    public async Task Carvana_APoolFilledOnTheLastPageTheSiteHasByItsStatedCount_IsNotCapped()
    {
        const string statedCount = "45 cars\n";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 21), [2] = CarvanaCards("b", 24) },
            new Dictionary<int, string> { [1] = statedCount });
        int capped = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 45, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None);

        Assert.Equal(45, pool.Count);
        Assert.Equal(0, capped);
    }

    [Fact]
    public async Task Carvana_ASiteThatRunsOutBeforeThePoolFills_IsNotCapped()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 9),
            [3] = CarvanaCards("b", 9),
        });
        int capped = 0;

        await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None);

        Assert.Equal(0, capped);
    }

    [Fact]
    public async Task CarsCom_APageWithMoreLinksThanThePoolHasRoomFor_ReportsTheCollectionAsCappedWhenEveryLinkIsVisitedAgain()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [.. Enumerable.Range(0, 5).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{i}/?sid=x", ""))],
        });
        int capped = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 3, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None, revisit: true);

        Assert.Equal(3, pool.Count);
        Assert.Equal(1, capped);
    }

    [Fact]
    public async Task CarsCom_APoolFilledByAFirstPageWithMoreLinksThanItHasRoomFor_IsCappedSincePagesAfterItWereNeverRead()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [.. Enumerable.Range(0, 5).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{i}/?sid=x", ""))],
        });
        int capped = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 3, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None);

        Assert.Equal(3, pool.Count);
        Assert.Single(browser.Loads);
        Assert.Equal(1, capped);
    }

    [Fact]
    public async Task CarsCom_APoolFilledExactlyByAFirstPage_IsCappedSinceNothingSaysThereIsNoSecondPage()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [.. Enumerable.Range(0, 3).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{i}/?sid=x", ""))],
        });
        int capped = 0;

        await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 3, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => capped++, CancellationToken.None);

        Assert.Single(browser.Loads);
        Assert.Equal(1, capped);
    }

    [Fact]
    public async Task Carvana_AFirstPageThatFillsThePoolIsNotFollowedByAnother()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 21), [2] = CarvanaCards("b", 21) });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 10, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

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

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(["https://www.carvana.com/vehicle/1?x=1"], pool);
        Assert.Equal(2, browser.Loads.Count);
    }

    [Fact]
    public async Task Carvana_AnEmptyFirstPageEndsTheSearchAfterOnePage()
    {
        var browser = new FakeBrowser([]);

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Empty(pool);
        Assert.Single(browser.Loads);
    }

    private static List<PageLink> CarsComCards(string prefix, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{prefix}{i}/?sid=x", ""))];

    [Fact]
    public async Task CarsCom_FollowsThreePagesAndStopsWhenAPageAddsNothingNew()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla&page_size=100";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarsComCards("a", 100),
            [2] = CarsComCards("b", 100),
            [3] = CarsComCards("c", 37),
            [4] = CarsComCards("c", 37),
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 400, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(
            [(search, 1), (search + "&page=2", 2), (search + "&page=3", 3), (search + "&page=4", 4)],
            browser.Loads);
        Assert.Equal(237, pool.Count);
        Assert.Contains("https://www.cars.com/vehicledetail/c36/?sid=x", pool);
    }

    [Fact]
    public async Task CarsCom_StopsPagingOnceThePoolIsFull()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla&page_size=100";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarsComCards("a", 100),
            [2] = CarsComCards("b", 100),
            [3] = CarsComCards("c", 100),
        });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 150, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(150, pool.Count);
    }

    [Fact]
    public async Task Carvana_ANoExactMatchesPageContributesNoLinksAndEndsPagingBeforeTheNextPage()
    {
        string pageOne = Fixture("carvana-insight-search.txt");
        string noExactMatches = Fixture("carvana-insight-search-no-exact-matches.txt");
        Assert.Contains("16 cars", pageOne);
        Assert.Contains("No exact matches", noExactMatches);
        List<PageLink> pageOneCards = CarvanaCards("a", 12);
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] = pageOneCards,
                [2] = [.. pageOneCards.Take(7), .. CarvanaCards("civic", 5)],
                [3] = [.. pageOneCards.Take(7), .. CarvanaCards("civic", 5)],
            },
            new Dictionary<int, string> { [1] = pageOne, [2] = noExactMatches, [3] = noExactMatches });
        List<int> exhaustedPages = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, NoneKnown, browser.LoadAsync, (_, _) => { }, exhaustedPages.Add, () => { }, CancellationToken.None);

        Assert.Equal(pageOneCards.Select(c => c.Href), pool);
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal([2], exhaustedPages);
    }

    [Fact]
    public async Task Carvana_AFirstPageThatSaysNoExactMatchesContributesNoLinks()
    {
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 12) },
            new Dictionary<int, string> { [1] = Fixture("carvana-insight-search-no-exact-matches.txt") });
        List<int> exhaustedPages = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, exhaustedPages.Add, () => { }, CancellationToken.None);

        Assert.Empty(pool);
        Assert.Single(browser.Loads);
        Assert.Equal([1], exhaustedPages);
    }

    [Fact]
    public async Task CarsCom_HasNoExhaustedSearchPatternSoAPageSayingSoIsTakenAsItComes()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = [.. Enumerable.Range(0, 5).Select(i => new PageLink($"https://www.cars.com/vehicledetail/{i}/?sid=x", ""))] },
            new Dictionary<int, string> { [1] = "No exact matches\n5 cars" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.CarsCom, search, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => Assert.Fail("cars.com has no exhausted pattern"), () => { }, CancellationToken.None);

        Assert.Equal(5, pool.Count);
    }

    [Fact]
    public async Task Carvana_AFirstPageStatingSixteenCarsButShowingEighteenContributesSixteen()
    {
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 18), [2] = CarvanaCards("b", 18) },
            new Dictionary<int, string> { [1] = Fixture("carvana-insight-search.txt"), [2] = "16 cars" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(16, pool.Count);
        Assert.Equal("https://www.carvana.com/vehicle/a15", pool[^1]);
        Assert.Single(browser.Loads);
    }

    [Fact]
    public async Task Carvana_TheFirstPagesStatedCountBoundsTheLinksAcrossEveryPageFollowed()
    {
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] = CarvanaCards("a", 21),
                [2] = CarvanaCards("b", 21),
                [3] = CarvanaCards("c", 21),
            },
            new Dictionary<int, string> { [1] = "Sort\n30 cars\n", [2] = "30 cars", [3] = "30 cars" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, 200, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(30, pool.Count);
        Assert.Equal("https://www.carvana.com/vehicle/a0", pool[0]);
        Assert.Equal("https://www.carvana.com/vehicle/b8", pool[^1]);
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
    }

    [Fact]
    public async Task Autotrader_AStatedCountBoundsItsOnePageAsItAlwaysDid()
    {
        const string search = "https://www.autotrader.com/cars-for-sale/used-cars/toyota/prius?zip=32114";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = [.. Enumerable.Range(1, 12).Select(i => new PageLink($"https://www.autotrader.com/cars-for-sale/vehicle/{i}?clickType=listing", ""))] },
            new Dictionary<int, string> { [1] = "5 Matches" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Autotrader, search, 60, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(5, pool.Count);
        Assert.Single(browser.Loads);
    }

    [Theory]
    [InlineData("16 cars\n\n16 cars", 16)]
    [InlineData("1 car\nChange Location", 1)]
    [InlineData("Applied Filters (4)\nCertified Cars\nSearch Cars", null)]
    public void Carvana_ReadsItsStatedCountFromTheSearchPageText(string text, int? expected)
    {
        Assert.Equal(expected, WalkSites.Carvana.MatchCountIn(text));
    }

    [Fact]
    public async Task Carvana_ALaterPageThatFailsEndsPagingAndKeepsTheLinksAlreadyCollected()
    {
        var pages = new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 21) };
        List<(int PageNumber, string Message)> failures = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            CarvanaSearch,
            200,
            NoneKnown,
            (_, pageNumber, _) => pageNumber == 1
                ? Task.FromResult(new SearchPageContent(pages[1]))
                : throw new TimeoutException("page 2 timed out"),
            (pageNumber, ex) => failures.Add((pageNumber, ex.Message)),
            _ => { },
            () => { },
            CancellationToken.None);

        Assert.Equal(21, pool.Count);
        Assert.Equal([(2, "page 2 timed out")], failures);
    }

    [Fact]
    public async Task Carvana_AThirdPageThatFails_ReportsPageThreeKeepsTwoPagesOfLinksAndDoesNotReportACap()
    {
        var pages = new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 21),
        };
        List<int> failedPages = [];
        int capped = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            CarvanaSearch,
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            (_, pageNumber, _) => pageNumber < 3
                ? Task.FromResult(new SearchPageContent(pages[pageNumber]))
                : throw new TimeoutException("page 3 timed out"),
            (pageNumber, _) => failedPages.Add(pageNumber),
            _ => { },
            () => capped++,
            CancellationToken.None);

        Assert.Equal(42, pool.Count);
        Assert.Equal([3], failedPages);
        Assert.Equal(0, capped);
    }

    [Fact]
    public async Task Carvana_AFailingFirstPageStillFailsTheSearch()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            CarvanaSearch,
            200,
            NoneKnown,
            (_, _, _) => throw new TimeoutException("page 1 timed out"),
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None));
    }

    [Fact]
    public async Task Carvana_CancellationDuringALaterPageIsNotSwallowed()
    {
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            CarvanaSearch,
            200,
            NoneKnown,
            (_, pageNumber, ct) =>
            {
                if (pageNumber == 1)
                {
                    return Task.FromResult(new SearchPageContent(CarvanaCards("a", 21)));
                }

                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new SearchPageContent([]));
            },
            (_, _) => Assert.Fail("a cancelled walk must not be reported as a failed page"),
            _ => { },
            () => { },
            cts.Token));
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
            (url, _, poolSize, ct) => WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, url, poolSize, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, ct),
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

    [Fact]
    public async Task Carvana_WithAnUnboundedPool_FollowsEveryPageTheSiteHasAndKeepsEveryLink()
    {
        Dictionary<int, List<PageLink>> pages = [];
        for (int page = 1; page <= 12; page++)
        {
            pages[page] = CarvanaCards($"p{page}-", 21);
        }

        var browser = new FakeBrowser(pages);

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(12 * 21, pool.Count);
        Assert.Equal(Enumerable.Range(1, 13), browser.Loads.Select(l => l.PageNumber));
    }

    [Fact]
    public async Task Carvana_WithAnUnboundedPool_StillStopsAtTheRecordedPagesStatedCount()
    {
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 18), [2] = CarvanaCards("b", 18) },
            new Dictionary<int, string> { [1] = Fixture("carvana-insight-search.txt"), [2] = "16 cars" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(16, pool.Count);
        Assert.Single(browser.Loads);
    }

    [Fact]
    public async Task Carvana_WithAnUnboundedPool_StopsWhenTheRecordedSearchRunsOutOfExactMatches()
    {
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = CarvanaCards("a", 21), [2] = CarvanaCards("b", 21) },
            new Dictionary<int, string> { [1] = "Sort\n40 cars\n", [2] = Fixture("carvana-insight-search-no-exact-matches.txt") });
        List<int> exhaustedPages = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, CarvanaSearch, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, exhaustedPages.Add, () => { }, CancellationToken.None);

        Assert.Equal(21, pool.Count);
        Assert.Equal([2], exhaustedPages);
    }

    [Fact]
    public async Task PagedSearchWithNoCap_VisitsEveryLinkOfEveryPage()
    {
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCards("a", 21),
            [2] = CarvanaCards("b", 21),
            [3] = CarvanaCards("c", 21),
            [4] = CarvanaCards("d", 5),
        });
        List<string> visited = [];

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.Carvana,
            [CarvanaSearch],
            maxDetailPages: null,
            (url, _, poolSize, ct) => WalkSearchPages.CollectLinksAsync(WalkSites.Carvana, url, poolSize, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, ct),
            (link, _, _) =>
            {
                visited.Add(link);
                return Task.FromResult(DetailPageOutcome.Upserted);
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(68, tally.Visited);
        Assert.Equal(68, visited.Distinct().Count());
        Assert.Equal([1, 2, 3, 4, 5], browser.Loads.Select(l => l.PageNumber));
    }
}
