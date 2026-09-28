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

    private sealed class FakeBrowser(Dictionary<int, List<PageLink>> pages, Dictionary<int, string>? texts = null, Dictionary<int, List<string>>? unrenderedHrefs = null)
    {
        public List<(string Url, int PageNumber)> Loads { get; } = [];

        public Task<SearchPageContent> LoadAsync(string url, int pageNumber, CancellationToken _)
        {
            Loads.Add((url, pageNumber));
            IReadOnlyList<PageLink> anchors = pages.TryGetValue(pageNumber, out List<PageLink>? served) ? served : [];
            string? text = texts is not null && texts.TryGetValue(pageNumber, out string? servedText) ? servedText : null;
            IReadOnlyList<string>? unrendered = unrenderedHrefs is not null && unrenderedHrefs.TryGetValue(pageNumber, out List<string>? servedUnrendered) ? servedUnrendered : null;
            return Task.FromResult(new SearchPageContent(anchors, text, UnrenderedHrefs: unrendered));
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

    private static List<PageLink> CarvanaCardsWithPrice(string prefix, int count, decimal price) =>
        [.. Enumerable.Range(0, count).Select(i => new PageLink(
            $"https://www.carvana.com/vehicle/{prefix}{i}",
            "",
            $"2020 Honda Insight\nEX\n38k miles\nCurrent price:\n${price:N0}\n$430/mo\nestimated\n$0 cash down\nFree shipping\nGet it today"))];

    [Fact]
    public async Task Carvana_APageWhereEveryCardIsOverThePriceCeiling_StillContinuesPagingToTheNext()
    {
        // A page-1 made entirely of cars over the ceiling used to read as "added nothing" and end the
        // paging right there, leaving every cheaper car on page 2 unread and every known posting on it
        // untouched.
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = CarvanaCardsWithPrice("a", 21, 30000m),
            [2] = CarvanaCardsWithPrice("b", 5, 20000m),
        });
        int overCeiling = 0;
        int capped = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana, CarvanaSearch, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync,
            (_, _) => { }, _ => { }, () => capped++, CancellationToken.None,
            maxPrice: 25000, onOverPriceCeiling: () => overCeiling++);

        Assert.Equal(21, overCeiling);
        Assert.Equal(5, pool.Count);
        Assert.Contains("https://www.carvana.com/vehicle/b0", pool);
        Assert.Equal(0, capped);
    }

    [Fact]
    public async Task CarsCom_APaddedPageRepeatingTheSameOverCeilingCards_AddsNothingAndStopsPaging()
    {
        // cars.com states no match count, so a page adding nothing new is the only thing that ends its
        // paging. Before the fix, an over-ceiling card counted toward "added" on every page it appeared
        // on, uncounted against the URLs already seen, so a padded page repeating the same over-ceiling
        // cars kept reading as "added something" and the search never stopped (lesson 6be298c0).
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight";
        List<PageLink> repeatedOverCeilingCards =
        [
            .. Enumerable.Range(0, 4).Select(i => new PageLink(
                $"https://www.cars.com/vehicledetail/far{i}/?sid=x",
                "",
                "Used 2022 Honda Insight EX\n$30,998\n90,924 mi.")),
        ];
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = repeatedOverCeilingCards,
            [2] = repeatedOverCeilingCards,
        });
        int overCeiling = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync,
            (_, _) => { }, _ => { }, () => { }, CancellationToken.None,
            maxPrice: 25000, onOverPriceCeiling: () => overCeiling++);

        // Page 2 repeats the same four cards page 1 already reported over the ceiling, so it adds
        // nothing new and paging stops there: a third page is never requested.
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(4, overCeiling);
        Assert.Empty(pool);
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
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
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
        const string search = "https://www.cars.com/shopping/results/?models[]=toyota-corolla";
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

    private static List<PageLink> AutotraderCards(int startingId, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new PageLink($"https://www.autotrader.com/cars-for-sale/vehicle/{startingId + i}?clickType=listing", ""))];

    [Fact]
    public async Task Autotrader_AStatedCountOverOnePageFollowsASecondPageByFirstRecordOffset()
    {
        // The shape reported from walk run 20260927-162013's corolla-hybrid search: 39 Matches, with
        // only the first page's 25 cards ever read before WalkSite.Autotrader had a PagedSearchUrl.
        const string search = "https://www.autotrader.com/cars-for-sale/used-cars/toyota/corolla?zip=32833";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = AutotraderCards(1000, 25), [2] = AutotraderCards(2000, 14) },
            new Dictionary<int, string> { [1] = "39 Matches", [2] = "39 Matches" });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(WalkSites.Autotrader, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { }, CancellationToken.None);

        Assert.Equal(39, pool.Count);
        Assert.Equal([search, $"{search}&firstRecord=25"], browser.Loads.Select(l => l.Url));
        Assert.Contains("https://www.autotrader.com/cars-for-sale/vehicle/1000?clickType=listing", pool);
        Assert.Contains("https://www.autotrader.com/cars-for-sale/vehicle/2013?clickType=listing", pool);
    }

    [Fact]
    public async Task Autotrader_ASecondPageThatFailsEndsPagingAndKeepsTheFirstPagesLinks()
    {
        const string search = "https://www.autotrader.com/cars-for-sale/used-cars/toyota/corolla?zip=32833";
        List<(int PageNumber, string Message)> failures = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Autotrader,
            search,
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            (_, pageNumber, _) => pageNumber == 1
                ? Task.FromResult(new SearchPageContent(AutotraderCards(1000, 25), "39 Matches"))
                : throw new TimeoutException("page 2 timed out"),
            (pageNumber, ex) => failures.Add((pageNumber, ex.Message)),
            _ => { },
            () => { },
            CancellationToken.None);

        Assert.Equal(25, pool.Count);
        Assert.Equal([(2, "page 2 timed out")], failures);
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
    public async Task CarsCom_APageOfOnlyBeyondRadiusCards_AddsNothingAndStopsPagingLikeAnEmptyPage()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
            [2] = [new PageLink("https://www.cars.com/vehicledetail/far/?sid=x", "Used 2020 Honda Insight EX", "Tampa, FL (95 mi)")],
            [3] = [new PageLink("https://www.cars.com/vehicledetail/never-read/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
        });
        int beyondRadius = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onBeyondRadius: () => beyondRadius++);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(1, beyondRadius);
    }

    [Fact]
    public async Task CarsCom_TheSameOutOfRadiusCarRepeatingAcrossPages_IsReportedOnlyOnce()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        List<PageLink> repeatingFarCard = [new("https://www.cars.com/vehicledetail/far/?sid=page-varies", "Used 2020 Honda Insight EX", "Tampa, FL (95 mi)")];
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [.. repeatingFarCard, new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
            [2] = [.. repeatingFarCard, new PageLink("https://www.cars.com/vehicledetail/another-far/?sid=x", "Used 2020 Honda Insight EX", "Lakeland, FL (65 mi)"), new PageLink("https://www.cars.com/vehicledetail/fresh-near/?sid=x", "Used 2020 Honda Insight EX", "Orlando, FL (10 mi)")],
            [3] = repeatingFarCard,
        });
        int beyondRadius = 0;

        await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onBeyondRadius: () => beyondRadius++);

        // Page 1 adds the real Sanford link, so paging continues past it; page 2's fresh Orlando link
        // also keeps it going. The repeating "far" card is beyond radius on every page it appears on, but
        // is counted once, not three times; page 2's distinct "another-far" card is a genuinely different
        // out-of-radius car and is counted on its own. Page 3 (only the repeat) adds nothing new and ends paging.
        Assert.Equal([1, 2, 3], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(2, beyondRadius);
    }

    [Fact]
    public async Task CarsCom_ACardWithNoStatedDistance_IsReportedSeparatelyFromBeyondRadius()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [new PageLink("https://www.cars.com/vehicledetail/nationwide/?sid=x", "", "")],
        });
        int beyondRadius = 0;
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onBeyondRadius: () => beyondRadius++, onNoDistance: () => noDistance++);

        Assert.Empty(pool);
        Assert.Equal(0, beyondRadius);
        Assert.Equal(1, noDistance);
    }

    [Fact]
    public async Task CarsCom_ALinkAlreadyPooled_IsNeverReportedAsOutOfRadiusWhenALaterPageMisreadsItsDistance()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            [1] = [new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
            // The same link, already in the pool, reappears carrying a neighboring card's wrapper text
            // (the same heuristic behind lesson 9514dea8); it must not be counted as skipped just because
            // this page's card text reads it as beyond radius.
            [2] = [new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Tampa, FL (95 mi)")],
        });
        int beyondRadius = 0;
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onBeyondRadius: () => beyondRadius++, onNoDistance: () => noDistance++);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(0, beyondRadius);
        Assert.Equal(0, noDistance);
    }

    [Fact]
    public async Task CarsCom_ALinkReportedSkippedOnAnEarlierPage_IsWithdrawnWhenALaterPagePoolsItFromItsOwnText()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        var browser = new FakeBrowser(new Dictionary<int, List<PageLink>>
        {
            // Page 1 misreads two links off a neighboring card's wrapper text (the same heuristic behind
            // lesson 9514dea8): one reads as beyond radius, the other as stating no distance at all
            // (its wrapper names more than one). A third, ordinary in-radius card keeps this page from
            // adding nothing, so paging continues to page 2.
            [1] =
            [
                new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                new PageLink("https://www.cars.com/vehicledetail/beyond/?sid=x", "", "Tampa, FL (95 mi)"),
                new PageLink("https://www.cars.com/vehicledetail/nodistance/?sid=x", "", "Tampa, FL (95 mi)\nLakeland, FL (65 mi)"),
            ],
            // Page 2 carries the same two links again, this time under their own single-card text, both
            // in radius.
            [2] =
            [
                new PageLink("https://www.cars.com/vehicledetail/beyond/?sid=y", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                new PageLink("https://www.cars.com/vehicledetail/nodistance/?sid=y", "Used 2020 Honda Insight EX", "Melbourne, FL (39 mi)"),
            ],
        });
        int beyondRadius = 0;
        int noDistance = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50,
            onBeyondRadius: () => beyondRadius++, onNoDistance: () => noDistance++,
            onBeyondRadiusWithdrawn: () => beyondRadius--, onNoDistanceWithdrawn: () => noDistance--);

        Assert.Equal(
            [
                "https://www.cars.com/vehicledetail/near/?sid=x",
                "https://www.cars.com/vehicledetail/beyond/?sid=y",
                "https://www.cars.com/vehicledetail/nodistance/?sid=y",
            ],
            pool);
        // Page 2 added two links the pool didn't already hold, so paging continued to page 3, which
        // (being unfixtured) added nothing and ended it there.
        Assert.Equal([1, 2, 3], browser.Loads.Select(l => l.PageNumber));

        // Both links were pooled from their own in-radius text on page 2, so neither earlier report
        // survives: the pair line must never count a car the walk kept as skipped.
        Assert.Equal(0, beyondRadius);
        Assert.Equal(0, noDistance);
    }

    [Fact]
    public async Task CarsCom_ACardStillUnrenderedAfterTheWait_IsNeitherPooledNorCountedAsBeyondRadiusOrNoDistance()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string unrenderedHref = "https://www.cars.com/vehicledetail/unrendered/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] =
                [
                    new PageLink(unrenderedHref, "", ""),
                    new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                ],
            },
            unrenderedHrefs: new Dictionary<int, List<string>> { [1] = [unrenderedHref] });
        int unrenderedBeyondRadius = 0;
        int unrenderedNoDistance = 0;
        int unrendered = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50,
            onBeyondRadius: () => unrenderedBeyondRadius++, onNoDistance: () => unrenderedNoDistance++, onUnrendered: _ => unrendered++);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal(0, unrenderedBeyondRadius);
        Assert.Equal(0, unrenderedNoDistance);
        Assert.Equal(1, unrendered);
    }

    [Fact]
    public async Task CarsCom_TheSameUnrenderedCardRepeatingAcrossPages_IsReportedOnlyOnce()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string unrenderedHref = "https://www.cars.com/vehicledetail/unrendered/?sid=page-varies";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] = [new PageLink(unrenderedHref, "", ""), new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
                [2] = [new PageLink(unrenderedHref, "", ""), new PageLink("https://www.cars.com/vehicledetail/fresh-near/?sid=x", "Used 2020 Honda Insight EX", "Orlando, FL (10 mi)")],
                [3] = [new PageLink(unrenderedHref, "", "")],
            },
            unrenderedHrefs: new Dictionary<int, List<string>>
            {
                [1] = [unrenderedHref],
                [2] = [unrenderedHref],
                [3] = [unrenderedHref],
            });
        int unrendered = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onUnrendered: _ => unrendered++);

        // Page 3 (only the repeating unrendered card) adds nothing new and ends paging, exactly as an
        // empty page does.
        Assert.Equal([1, 2, 3], browser.Loads.Select(l => l.PageNumber));
        Assert.Equal(2, pool.Count);
        Assert.Equal(1, unrendered);
    }

    [Fact]
    public async Task CarsCom_APageOfOnlyUnrenderedCards_AddsNothingAndStopsPagingLikeAnEmptyPage()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string unrenderedOnPageTwo = "https://www.cars.com/vehicledetail/unrendered/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] = [new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
                [2] = [new PageLink(unrenderedOnPageTwo, "", "")],
                [3] = [new PageLink("https://www.cars.com/vehicledetail/never-read/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)")],
            },
            unrenderedHrefs: new Dictionary<int, List<string>> { [2] = [unrenderedOnPageTwo] });

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal([1, 2], browser.Loads.Select(l => l.PageNumber));
    }

    [Fact]
    public async Task CarsCom_AnUnrenderedCardThatRendersOnALaterPage_IsNotCountedUnrendered()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string resolvesLaterHref = "https://www.cars.com/vehicledetail/resolves-later/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] =
                [
                    new PageLink(resolvesLaterHref, "", ""),
                    new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                ],
                [2] =
                [
                    new PageLink(resolvesLaterHref, "Used 2020 Honda Insight EX", "Melbourne, FL (39 mi)"),
                    new PageLink("https://www.cars.com/vehicledetail/fresh-near/?sid=x", "Used 2020 Honda Insight EX", "Orlando, FL (10 mi)"),
                ],
            },
            unrenderedHrefs: new Dictionary<int, List<string>> { [1] = [resolvesLaterHref] });
        int unrendered = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onUnrendered: _ => unrendered++);

        Assert.Contains(resolvesLaterHref, pool);
        Assert.Equal(0, unrendered);
    }

    [Fact]
    public async Task CarsCom_AnUnrenderedCardThatIsBeyondRadiusOnALaterPage_IsNotCountedUnrendered()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string resolvesFarHref = "https://www.cars.com/vehicledetail/resolves-far/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] =
                [
                    new PageLink(resolvesFarHref, "", ""),
                    new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                ],
                [2] = [new PageLink(resolvesFarHref, "Used 2020 Honda Insight EX", "Tampa, FL (95 mi)")],
            },
            unrenderedHrefs: new Dictionary<int, List<string>> { [1] = [resolvesFarHref] });
        int unrendered = 0;
        int beyondRadius = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onBeyondRadius: () => beyondRadius++, onUnrendered: _ => unrendered++);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal(0, unrendered);
        Assert.Equal(1, beyondRadius);
    }

    [Fact]
    public async Task CarsCom_AKnownLinkThatStaysUnrendered_IsNotTouchedSoTheLedgerNeverCreditsAnUnmeasuredCard()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string knownUnrenderedHref = "https://www.cars.com/vehicledetail/known-unrendered/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>> { [1] = [new PageLink(knownUnrenderedHref, "", "")] },
            unrenderedHrefs: new Dictionary<int, List<string>> { [1] = [knownUnrenderedHref] });
        List<(string CanonicalUrl, decimal? Price, int BadgeCount)> touches = [];
        ValueTask<bool> TouchKnownAsync(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken _)
        {
            touches.Add((canonicalUrl, cardPrice, cardBadges.Count));
            return ValueTask.FromResult(true);
        }
        List<string> unrenderedUrls = [];

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, TouchKnownAsync, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onUnrendered: unrenderedUrls.Add);

        Assert.Empty(pool);
        // The canonical URL is what lets a caller (WalkCommand) check whether the ledger already
        // holds this exact link and, if so, record the pair's coverage as partial for it.
        Assert.Equal([WalkSites.CanonicalDetailUrl(knownUnrenderedHref)], unrenderedUrls);
        Assert.Empty(touches);
    }

    [Fact]
    public async Task CarsCom_AnEmptyHrefAnchorOnAPageThatGaveUpTheRenderWait_IsSkippedRatherThanThrowing()
    {
        const string search = "https://www.cars.com/shopping/results/?models[]=honda-insight&maximum_distance=50";
        const string unrenderedHref = "https://www.cars.com/vehicledetail/unrendered/?sid=x";
        var browser = new FakeBrowser(
            new Dictionary<int, List<PageLink>>
            {
                [1] =
                [
                    new PageLink("", "", ""),
                    new PageLink(unrenderedHref, "", ""),
                    new PageLink("https://www.cars.com/vehicledetail/near/?sid=x", "Used 2020 Honda Insight EX", "Sanford, FL (28 mi)"),
                ],
            },
            unrenderedHrefs: new Dictionary<int, List<string>> { [1] = [unrenderedHref] });
        int unrendered = 0;

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarsCom, search, WalkPairSearches.UnboundedPool, NoneKnown, browser.LoadAsync, (_, _) => { }, _ => { }, () => { },
            CancellationToken.None, revisit: false, maxDistanceMiles: 50, onUnrendered: _ => unrendered++);

        Assert.Equal(["https://www.cars.com/vehicledetail/near/?sid=x"], pool);
        Assert.Equal(1, unrendered);
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
