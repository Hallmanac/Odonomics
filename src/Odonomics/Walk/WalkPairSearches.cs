namespace Odonomics.Walk;

/// <summary>
/// Walks every search page one (site, model) pair needs, in order, and reports them as one pair.
/// A site's <see cref="WalkSite.BuildSearchUrls"/> usually returns one URL, and then this behaves
/// exactly like a single <see cref="WalkDetailWalk.RunAsync"/> over that page's links; for a model
/// that needs more (cars.com's hybrid facet and base-model facet for a hybrid-only-from-year model)
/// the per-pair cap is split across the searches and each search's own links are walked in turn,
/// so the pair still never spends more than the cap in total.
/// </summary>
public static class WalkPairSearches
{
    /// <summary>The cap's share for each of <paramref name="searchCount"/> searches: an even split,
    /// with any remainder going to the earliest searches one apiece (so of two searches, the odd
    /// page goes to the first, which is the hybrid-facet search on cars.com).</summary>
    public static IReadOnlyList<int> SplitCap(int maxDetailPages, int searchCount) =>
        [.. Enumerable.Range(0, searchCount).Select(i => (maxDetailPages / searchCount) + (i < maxDetailPages % searchCount ? 1 : 0))];

    /// <summary>The recorder file name for the search page at <paramref name="searchIndex"/>:
    /// "search.txt" for the first, so a one-search pair writes exactly what it always did, then
    /// "search-2.txt", "search-3.txt" and so on.</summary>
    public static string SearchFileName(int searchIndex) => searchIndex == 0 ? "search.txt" : $"search-{searchIndex + 1}.txt";

    /// <summary>Walks each of <paramref name="searchUrls"/> in turn. <paramref name="collectLinksAsync"/>
    /// opens one search page (given its URL and index) and returns the candidate detail links to
    /// consider from it, at most the pool size it is handed: the search's cap share times the
    /// site's over-fetch multiplier. <paramref name="visitLinkAsync"/> is given a running index
    /// across the whole pair rather than one per search, so the detail files a pair records never
    /// collide. A search whose cap share is zero (a cap of 1 across two searches) is not opened.
    /// The returned tally is the sum over every search.</summary>
    public static async Task<DetailWalkTally> RunAsync(
        WalkSite site,
        IReadOnlyList<string> searchUrls,
        int maxDetailPages,
        Func<string, int, int, CancellationToken, Task<IReadOnlyList<string>>> collectLinksAsync,
        Func<string, int, CancellationToken, Task<DetailPageOutcome>> visitLinkAsync,
        Func<CancellationToken, Task> gapBeforeNextLinkAsync,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<int> shares = SplitCap(maxDetailPages, searchUrls.Count);
        var total = new DetailWalkTally(0, 0, new DroppedBreakdown(0, 0, 0, 0));
        for (int search = 0; search < searchUrls.Count; search++)
        {
            if (shares[search] == 0)
            {
                continue;
            }

            IReadOnlyList<string> candidateLinks = await collectLinksAsync(
                searchUrls[search],
                search,
                shares[search] * site.DetailLinkOverfetchMultiplier,
                cancellationToken);
            int visitedBefore = total.Visited;
            DetailWalkTally tally = await WalkDetailWalk.RunAsync(
                candidateLinks,
                shares[search],
                (link, i, ct) => visitLinkAsync(link, visitedBefore + i, ct),
                gapBeforeNextLinkAsync,
                cancellationToken);
            total = total.Plus(tally);
        }

        return total;
    }
}
