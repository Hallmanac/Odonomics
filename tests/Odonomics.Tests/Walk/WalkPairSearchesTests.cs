using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves a pair with more than one search page shares the per-pair cap across them and
/// still reports as one pair, while a pair with one search behaves exactly as a bare
/// <see cref="WalkDetailWalk"/> over that page. The first search page is the real recorded cars.com
/// page (its used-card titles become the anchors a live card has); the second is a synthetic page
/// of base-model cards.</summary>
public partial class WalkPairSearchesTests
{
    [GeneratedRegex(@"^(New|Used|Certified) \d{4} .+$")]
    private static partial Regex CardTitleLine();

    private static string FixturePath(string name) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name);

    private static List<PageLink> RecordedSearchAnchors(string idPrefix)
    {
        List<string> titles = [.. File.ReadAllLines(FixturePath("cars-com-search-with-new-cards.txt")).Where(l => CardTitleLine().IsMatch(l))];
        List<PageLink> anchors = [];
        for (int i = 0; i < titles.Count; i++)
        {
            anchors.Add(new PageLink($"https://www.cars.com/vehicledetail/{idPrefix}-{i}/?sid=abc", ""));
            anchors.Add(new PageLink($"https://www.cars.com/vehicledetail/{idPrefix}-{i}/?openLeadForm=true&sid=abc", titles[i]));
        }

        return anchors;
    }

    private sealed class Run
    {
        public List<string> OpenedSearches { get; } = [];
        public List<int> PoolSizes { get; } = [];
        public List<int> SearchIndexes { get; } = [];
        public List<(string Link, int Index)> Visits { get; } = [];
        public int Gaps { get; private set; }
        public List<string> Events { get; } = [];

        /// <summary>How many candidate links a search page offers, by facet prefix; a search not
        /// listed offers as many as it is asked for.</summary>
        public Dictionary<string, int> LinksOffered { get; } = [];

        public Task<IReadOnlyList<string>> CollectAsync(string url, int searchIndex, int poolSize, CancellationToken _)
        {
            OpenedSearches.Add(url);
            Events.Add("open");
            SearchIndexes.Add(searchIndex);
            PoolSizes.Add(poolSize);
            string prefix = url.Contains("hybrid", StringComparison.Ordinal) ? "hyb" : "base";
            IReadOnlyList<string> links = WalkSites.CarsCom.CollectDetailLinks(RecordedSearchAnchors(prefix), poolSize);
            return Task.FromResult<IReadOnlyList<string>>(
                LinksOffered.TryGetValue(prefix, out int offered) ? [.. links.Take(offered)] : links);
        }

        public Task<DetailPageOutcome> VisitAsync(string link, int index, CancellationToken _)
        {
            Visits.Add((link, index));
            Events.Add("visit");
            return Task.FromResult(DetailPageOutcome.Upserted);
        }

        public Task GapAsync(CancellationToken _)
        {
            Gaps++;
            Events.Add("gap");
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(30, 2, new[] { 15, 15 })]
    [InlineData(31, 2, new[] { 16, 15 })]
    [InlineData(1, 2, new[] { 1, 0 })]
    [InlineData(30, 1, new[] { 30 })]
    [InlineData(7, 3, new[] { 3, 2, 2 })]
    public void SplitCap_GivesTheOddPageToTheEarliestSearch(int cap, int searches, int[] expected)
    {
        Assert.Equal(expected, WalkPairSearches.SplitCap(cap, searches));
    }

    [Fact]
    public void SearchFileName_FirstSearchKeepsTheOriginalName()
    {
        Assert.Equal("search.txt", WalkPairSearches.SearchFileName(0));
        Assert.Equal("search-2.txt", WalkPairSearches.SearchFileName(1));
    }

    [Fact]
    public async Task RunAsync_TwoSearches_WalksBothAndSplitsTheCapHalfEach()
    {
        var run = new Run();

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 10,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(["https://x/hybrid", "https://x/base"], run.OpenedSearches);
        Assert.Equal([0, 1], run.SearchIndexes);
        Assert.Equal([10, 10], run.PoolSizes);
        Assert.Equal(10, tally.Visited);
        Assert.Equal(10, tally.Upserted);
        Assert.Equal(5, run.Visits.Count(v => v.Link.Contains("/hyb-", StringComparison.Ordinal)));
        Assert.Equal(5, run.Visits.Count(v => v.Link.Contains("/base-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_OddCap_GivesTheExtraPageToTheFirstSearch()
    {
        var run = new Run();

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 7,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(7, tally.Visited);
        Assert.Equal(4, run.Visits.Count(v => v.Link.Contains("/hyb-", StringComparison.Ordinal)));
        Assert.Equal(3, run.Visits.Count(v => v.Link.Contains("/base-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_TwoSearches_NumbersDetailPagesAcrossThePairSoRecordedFilesNeverCollide()
    {
        var run = new Run();

        await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 6,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(Enumerable.Range(0, 6), run.Visits.Select(v => v.Index));
    }

    [Fact]
    public async Task RunAsync_MismatchesOnTheFirstSearchDoNotSpendTheSecondSearchsShare()
    {
        var run = new Run();

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 6,
            run.CollectAsync,
            (link, index, ct) => Task.FromResult(
                link.Contains("/hyb-", StringComparison.Ordinal) && index < 3 ? DetailPageOutcome.NotMatching : DetailPageOutcome.Upserted),
            run.GapAsync,
            CancellationToken.None);

        Assert.Equal(3 + 3 + 3, tally.Visited);
        Assert.Equal(6, tally.Upserted);
        Assert.Equal(3, tally.Dropped.NotMatching);
    }

    [Fact]
    public async Task RunAsync_ASearchThatRunsOutOfLinks_RollsItsUnspentShareForwardToTheNext()
    {
        var run = new Run();
        run.LinksOffered["hyb"] = 4;

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 10,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal([10, 12], run.PoolSizes);
        Assert.Equal(10, tally.Visited);
        Assert.Equal(4, run.Visits.Count(v => v.Link.Contains("/hyb-", StringComparison.Ordinal)));
        Assert.Equal(6, run.Visits.Count(v => v.Link.Contains("/base-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task RunAsync_ALaterSearchThatRunsOutOfLinks_LeavesItsUnspentShareToTheEarlierSearchsUnvisitedLinks()
    {
        var run = new Run();
        run.LinksOffered["base"] = 3;

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 10,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(["https://x/hybrid", "https://x/base"], run.OpenedSearches);
        Assert.Equal(10, tally.Visited);
        Assert.Equal(10, tally.Upserted);
        Assert.Equal(7, run.Visits.Count(v => v.Link.Contains("/hyb-", StringComparison.Ordinal)));
        Assert.Equal(3, run.Visits.Count(v => v.Link.Contains("/base-", StringComparison.Ordinal)));
        Assert.Equal(Enumerable.Range(0, 10), run.Visits.Select(v => v.Index));
        Assert.Equal(run.Visits.Count, run.Visits.Select(v => v.Link).Distinct().Count());
    }

    [Fact]
    public async Task RunAsync_EverySearchExhausted_StopsShortOfTheCap()
    {
        var run = new Run();
        run.LinksOffered["hyb"] = 4;
        run.LinksOffered["base"] = 3;

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 10,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(7, tally.Visited);
    }

    [Fact]
    public async Task RunAsync_PacesTheStepBetweenOneSearchsLastVisitAndTheNextSearchPage()
    {
        var run = new Run();

        await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 4,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        int secondOpen = run.Events.IndexOf("open", run.Events.IndexOf("open") + 1);
        Assert.Equal("gap", run.Events[secondOpen - 1]);
        Assert.Equal("visit", run.Events[secondOpen - 2]);
    }

    [Fact]
    public async Task RunAsync_CapOfOneAcrossTwoSearches_NeverOpensTheSecondSearch()
    {
        var run = new Run();

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/hybrid", "https://x/base"], maxDetailPages: 1,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);

        Assert.Equal(["https://x/hybrid"], run.OpenedSearches);
        Assert.Equal(1, tally.Visited);
    }

    [Fact]
    public async Task RunAsync_OneSearch_BehavesLikeABareDetailWalk()
    {
        var run = new Run();
        var bare = new Run();
        IReadOnlyList<string> pool = WalkSites.CarsCom.CollectDetailLinks(RecordedSearchAnchors("base"), 20);

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.CarsCom, ["https://x/base"], maxDetailPages: 10,
            run.CollectAsync, run.VisitAsync, run.GapAsync, CancellationToken.None);
        DetailWalkTally expected = await WalkDetailWalk.RunAsync(pool, 10, bare.VisitAsync, bare.GapAsync, CancellationToken.None);

        Assert.Equal([20], run.PoolSizes);
        Assert.Equal([0], run.SearchIndexes);
        Assert.Equal(expected, tally);
        Assert.Equal(bare.Visits, run.Visits);
        Assert.Equal(bare.Gaps, run.Gaps);
    }
}
