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
    /// collide.
    ///
    /// <para>A search's share is an even split of the cap still unspent across the searches still
    /// to run, so whatever an earlier search leaves unspent (its page ran out of candidates) rolls
    /// forward to the next. A search that stops at its share may still have candidates it never
    /// visited, so once every search has run, any cap still unspent goes to those leftover links
    /// in search order. Together the two mean the pair falls short of the cap only when every
    /// search's links are exhausted, exactly as a single search does. No search page is opened
    /// once the cap is spent, and the returned tally is the sum over every search.</para>
    ///
    /// <para><paramref name="gapBeforeNextLinkAsync"/> runs between links inside a search and also
    /// wherever the walk moves from one search's last visit to the next page it loads, so a pair's
    /// pacing has no machine-speed step between searches.</para></summary>
    public static async Task<DetailWalkTally> RunAsync(
        WalkSite site,
        IReadOnlyList<string> searchUrls,
        int maxDetailPages,
        Func<string, int, int, CancellationToken, Task<IReadOnlyList<string>>> collectLinksAsync,
        Func<string, int, CancellationToken, Task<DetailPageOutcome>> visitLinkAsync,
        Func<CancellationToken, Task> gapBeforeNextLinkAsync,
        CancellationToken cancellationToken)
    {
        var total = new DetailWalkTally(0, 0, new DroppedBreakdown(0, 0, 0, 0));
        int remaining = maxDetailPages;
        List<IReadOnlyList<string>> unvisitedLinks = [];

        async Task<DetailWalkTally> WalkAsync(IReadOnlyList<string> links, int cap)
        {
            int visitedBefore = total.Visited;
            return await WalkDetailWalk.RunAsync(
                links,
                cap,
                (link, i, ct) => visitLinkAsync(link, visitedBefore + i, ct),
                gapBeforeNextLinkAsync,
                cancellationToken);
        }

        for (int search = 0; search < searchUrls.Count && remaining > 0; search++)
        {
            int share = SplitCap(remaining, searchUrls.Count - search)[0];
            if (total.Visited > 0)
            {
                await gapBeforeNextLinkAsync(cancellationToken);
            }

            IReadOnlyList<string> candidateLinks = await collectLinksAsync(
                searchUrls[search],
                search,
                share * site.DetailLinkOverfetchMultiplier,
                cancellationToken);
            DetailWalkTally tally = await WalkAsync(candidateLinks, share);
            total = total.Plus(tally);
            remaining -= tally.SpentOnCap;
            unvisitedLinks.Add([.. candidateLinks.Skip(tally.Visited)]);
        }

        foreach (IReadOnlyList<string> links in unvisitedLinks)
        {
            if (remaining <= 0)
            {
                break;
            }

            if (links.Count == 0)
            {
                continue;
            }

            await gapBeforeNextLinkAsync(cancellationToken);
            DetailWalkTally tally = await WalkAsync(links, remaining);
            total = total.Plus(tally);
            remaining -= tally.SpentOnCap;
        }

        return total;
    }
}
