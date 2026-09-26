namespace Odonomics.Walk;

/// <summary>
/// Collects one search's candidate detail links across as many result pages as the site has and
/// the pool needs. A site whose <see cref="WalkSite.PagedSearchUrl"/> is null (cars.com) has one
/// page per search, so this loads it once and returns what <see cref="WalkSite.CollectDetailLinks"/>
/// finds there. A paged site (carvana renders about 21 cards a page, and its inventory for a model
/// runs to a hundred or more) is followed page by page until the pool is full or a page adds no
/// link the pool did not already hold, which also ends the walk of a search whose last page repeats
/// or comes back empty. A site that states how many cars the search matches (see
/// <see cref="WalkSite.MatchCountPattern"/>) has its first page's count taken as the bound on the
/// links every page together contributes, and a page that says the search ran out of exact matches
/// (see <see cref="WalkSite.ExhaustedSearchPattern"/>) contributes nothing and ends the paging, since
/// what follows that notice is similar vehicles the facets never asked for. Only the first page is
/// required: a later page is a bonus, so one that fails to load ends the paging with the links already
/// collected instead of failing the search. A link the caller says it already knows (the ledger holds
/// it) is handed to <c>touchKnownAsync</c> with its card's asking price and never enters the pool, so it
/// spends none of the pool; paging goes on past pages made only of known links, and stops when the pool of
/// new links is full, the stated count is reached, or a page shows nothing not seen before.
/// A pool size of <see cref="WalkPairSearches.UnboundedPool"/> is the uncapped walk: the pool never fills,
/// so paging ends only by the stated count, a page that adds nothing, an exhausted search, or a failed page.
/// </summary>
public static class WalkSearchPages
{
    /// <summary>The candidate links for <paramref name="searchUrl"/>, in page order, one per
    /// canonical URL and at most <paramref name="poolSize"/>, not counting a link
    /// <paramref name="touchKnownAsync"/> takes (it is given the link's canonical URL and the card's
    /// asking price, or null when the card shows none, and returns true for a link it took). <paramref name="loadPageAsync"/> is
    /// given a page's URL and its 1-based number, and does everything a person would on that page
    /// (open it, scroll, dwell, record it) before returning its anchors and text, so the pacing between
    /// pages is the pacing between any two page loads. When a page after the first throws (other than
    /// because <paramref name="cancellationToken"/> was cancelled), <paramref name="onLaterPageFailed"/>
    /// is told its number and the exception and the pool built so far is returned; a failure on the
    /// first page still propagates, since without it the search has nothing. When a page says the search
    /// ran out of exact matches, <paramref name="onSearchExhausted"/> is told its number.</summary>
    public static async Task<IReadOnlyList<string>> CollectLinksAsync(
        WalkSite site,
        string searchUrl,
        int poolSize,
        Func<string, decimal?, CancellationToken, ValueTask<bool>> touchKnownAsync,
        Func<string, int, CancellationToken, Task<SearchPageContent>> loadPageAsync,
        Action<int, Exception> onLaterPageFailed,
        Action<int> onSearchExhausted,
        CancellationToken cancellationToken)
    {
        Func<string, int, string>? pageUrlFor = site.PagedSearchUrl;
        List<string> pool = [];
        HashSet<string> canonicalUrls = [];
        int linkBound = int.MaxValue;
        int considered = 0;

        for (int pageNumber = 1; ; pageNumber++)
        {
            string pageUrl = pageNumber > 1 && pageUrlFor is not null
                ? pageUrlFor(searchUrl, pageNumber)
                : searchUrl;
            SearchPageContent content;
            try
            {
                content = await loadPageAsync(pageUrl, pageNumber, cancellationToken);
            }
            catch (Exception ex) when (pageNumber > 1 && !cancellationToken.IsCancellationRequested)
            {
                onLaterPageFailed(pageNumber, ex);
                break;
            }

            if (site.SearchRanOutOfMatches(content.Text))
            {
                onSearchExhausted(pageNumber);
                break;
            }

            if (pageNumber == 1 && site.MatchCountIn(content.Text) is int statedCount)
            {
                linkBound = statedCount;
            }

            int added = 0;
            foreach (PageLink card in site.CollectDetailCards(content.Links, int.MaxValue, content.Text))
            {
                if (considered >= linkBound)
                {
                    break;
                }

                string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
                if (!canonicalUrls.Add(canonicalUrl))
                {
                    continue;
                }

                considered++;
                added++;
                if (!await touchKnownAsync(canonicalUrl, site.ReadCardPrice(card.CardText), cancellationToken) && pool.Count < poolSize)
                {
                    pool.Add(card.Href);
                }
            }

            if (pageUrlFor is null || pool.Count >= poolSize || considered >= linkBound || added == 0)
            {
                break;
            }
        }

        return pool;
    }
}
