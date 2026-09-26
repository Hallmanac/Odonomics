namespace Odonomics.Walk;

/// <summary>
/// Walks every search page one (site, model) pair needs, in order, and reports them as one pair.
/// A site's <see cref="WalkSite.BuildSearchUrls"/> usually returns one URL, and then this behaves
/// exactly like a single <see cref="WalkDetailWalk.RunAsync"/> over that page's links; for a model
/// that needs more (cars.com's hybrid facet and base-model facet for a hybrid-only-from-year model)
/// the per-pair cap is split across the searches and each search's own links are walked in turn,
/// so the pair still never spends more than the cap in total. With no cap at all (the walk's default)
/// there is nothing to split: every search's whole link pool is collected and every link not already
/// known is visited. A capped pair whose cap ended the walk with a search never opened, or (only when
/// every link is visited again) with collected links never visited, says so on its tally
/// (<see cref="DetailWalkTally.Capped"/>), so the run can record that pair's coverage as partial.
/// Without <c>revisit</c> the links a cap leaves unvisited are all new to the ledger, so they say
/// nothing about a posting the ledger holds.
/// </summary>
public static class WalkPairSearches
{
    /// <summary>The cap's share for each of <paramref name="searchCount"/> searches: an even split,
    /// with any remainder going to the earliest searches one apiece (so of two searches, the odd
    /// page goes to the first, which is the hybrid-facet search on cars.com).</summary>
    public static IReadOnlyList<int> SplitCap(int maxDetailPages, int searchCount) =>
        [.. Enumerable.Range(0, searchCount).Select(i => (maxDetailPages / searchCount) + (i < maxDetailPages % searchCount ? 1 : 0))];

    /// <summary>The link pool size that means "no bound": <see cref="WalkSearchPages.CollectLinksAsync"/>
    /// then follows a paged site until its own stop rules end it, and reads every link a page has.</summary>
    public const int UnboundedPool = int.MaxValue;

    /// <summary>The recorder file name for the search page at <paramref name="searchIndex"/>:
    /// "search.txt" for the first, so a one-search pair writes exactly what it always did, then
    /// "search-2.txt", "search-3.txt" and so on.</summary>
    public static string SearchFileName(int searchIndex) => searchIndex == 0 ? "search.txt" : $"search-{searchIndex + 1}.txt";

    /// <summary>The recorder file name for the detail links, with their card text, of the search page at
    /// <paramref name="searchIndex"/>: "cards.json" beside "search.txt", then "cards-2.json" and so on.</summary>
    public static string CardsFileName(int searchIndex) => searchIndex == 0 ? "cards.json" : $"cards-{searchIndex + 1}.json";

    /// <summary>Walks each of <paramref name="searchUrls"/> in turn. <paramref name="collectLinksAsync"/>
    /// opens one search page (given its URL and index) and returns the candidate detail links to
    /// consider from it, at most the pool size it is handed: the search's cap share times the
    /// site's over-fetch multiplier, or <see cref="UnboundedPool"/> when <paramref name="maxDetailPages"/>
    /// is null. <paramref name="visitLinkAsync"/> is given a running index
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
    /// pacing has no machine-speed step between searches.</para>
    ///
    /// <para>With a null <paramref name="maxDetailPages"/> the walk is uncapped: every search is
    /// collected before the first visit (a gap between searches keeps their page loads paced), a link
    /// two searches both carry is kept once, and the whole list is then visited in search order.
    /// <paramref name="announceVisits"/>, when given, is told once, before the pair's first detail
    /// visit, how many detail pages the pair is about to visit and whether the count is only what the
    /// first search's own walk starts with (a capped pair with searches still to run, whose later
    /// searches are not known yet). An uncapped pair's count is always the whole pair's.</para>
    ///
    /// <para><paramref name="revisit"/> says the pool holds links the ledger already knows, so a link the
    /// cap leaves unvisited is a posting the walk did not touch and makes the pair capped.</para></summary>
    public static async Task<DetailWalkTally> RunAsync(
        WalkSite site,
        IReadOnlyList<string> searchUrls,
        int? maxDetailPages,
        Func<string, int, int, CancellationToken, Task<IReadOnlyList<string>>> collectLinksAsync,
        Func<string, int, CancellationToken, Task<DetailPageOutcome>> visitLinkAsync,
        Func<CancellationToken, Task> gapBeforeNextLinkAsync,
        CancellationToken cancellationToken,
        Action<int, bool>? announceVisits = null,
        bool revisit = false)
    {
        if (maxDetailPages is not int cap)
        {
            return await RunUncappedAsync(searchUrls, collectLinksAsync, visitLinkAsync, gapBeforeNextLinkAsync, announceVisits, cancellationToken);
        }

        var total = new DetailWalkTally(0, 0, new DroppedBreakdown(0, 0, 0, 0));
        int remaining = cap;
        bool announced = false;
        List<IReadOnlyList<string>> unvisitedLinks = [];

        async Task<DetailWalkTally> WalkAsync(IReadOnlyList<string> links, int linkCap)
        {
            int visitedBefore = total.Visited;
            return await WalkDetailWalk.RunAsync(
                links,
                linkCap,
                (link, i, ct) => visitLinkAsync(link, visitedBefore + i, ct),
                gapBeforeNextLinkAsync,
                cancellationToken);
        }

        int searchesRun = 0;
        for (int search = 0; search < searchUrls.Count && remaining > 0; search++)
        {
            searchesRun++;
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
            if (!announced && candidateLinks.Count > 0)
            {
                announced = true;
                announceVisits?.Invoke(Math.Min(share, candidateLinks.Count), search < searchUrls.Count - 1);
            }

            DetailWalkTally tally = await WalkAsync(candidateLinks, share);
            total = total.Plus(tally);
            remaining -= tally.SpentOnCap;
            unvisitedLinks.Add([.. candidateLinks.Skip(tally.Visited)]);
        }

        if (!announced)
        {
            announceVisits?.Invoke(0, false);
        }

        bool capped = searchesRun < searchUrls.Count;
        foreach (IReadOnlyList<string> links in unvisitedLinks)
        {
            if (links.Count == 0)
            {
                continue;
            }

            if (remaining <= 0)
            {
                capped |= revisit;
                break;
            }

            await gapBeforeNextLinkAsync(cancellationToken);
            DetailWalkTally tally = await WalkAsync(links, remaining);
            total = total.Plus(tally);
            remaining -= tally.SpentOnCap;
            capped |= revisit && tally.Visited < links.Count;
        }

        return total with { Capped = capped };
    }

    private static async Task<DetailWalkTally> RunUncappedAsync(
        IReadOnlyList<string> searchUrls,
        Func<string, int, int, CancellationToken, Task<IReadOnlyList<string>>> collectLinksAsync,
        Func<string, int, CancellationToken, Task<DetailPageOutcome>> visitLinkAsync,
        Func<CancellationToken, Task> gapBeforeNextLinkAsync,
        Action<int, bool>? announceVisits,
        CancellationToken cancellationToken)
    {
        List<string> links = [];
        HashSet<string> canonicalUrls = [];
        for (int search = 0; search < searchUrls.Count; search++)
        {
            if (search > 0)
            {
                await gapBeforeNextLinkAsync(cancellationToken);
            }

            foreach (string link in await collectLinksAsync(searchUrls[search], search, UnboundedPool, cancellationToken))
            {
                if (canonicalUrls.Add(WalkSites.CanonicalDetailUrl(link)))
                {
                    links.Add(link);
                }
            }
        }

        announceVisits?.Invoke(links.Count, false);
        return await WalkDetailWalk.RunAsync(links, UnboundedPool, visitLinkAsync, gapBeforeNextLinkAsync, cancellationToken);
    }
}
