namespace Odonomics.Walk;

/// <summary>
/// Collects one search's candidate detail links across as many result pages as the site has and
/// the pool needs. A site whose <see cref="WalkSite.PagedSearchUrl"/> is null (cars.com) has one
/// page per search, so this loads it once and returns what <see cref="WalkSite.CollectDetailLinks"/>
/// finds there. A paged site (carvana renders about 21 cards a page, and its inventory for a model
/// runs to a hundred or more) is followed page by page until the pool is full or a page adds no
/// link the pool did not already hold, which also ends the walk of a search whose last page repeats
/// or comes back empty. Only the first page is required: a later page is a bonus, so one that fails
/// to load ends the paging with the links already collected instead of failing the search.
/// </summary>
public static class WalkSearchPages
{
    /// <summary>The candidate links for <paramref name="searchUrl"/>, in page order, one per
    /// canonical URL and at most <paramref name="poolSize"/>. <paramref name="loadPageAsync"/> is
    /// given a page's URL and its 1-based number, and does everything a person would on that page
    /// (open it, scroll, dwell, record it) before returning its anchors, so the pacing between
    /// pages is the pacing between any two page loads. When a page after the first throws (other than
    /// because <paramref name="cancellationToken"/> was cancelled), <paramref name="onLaterPageFailed"/>
    /// is told its number and the exception and the pool built so far is returned; a failure on the
    /// first page still propagates, since without it the search has nothing.</summary>
    public static async Task<IReadOnlyList<string>> CollectLinksAsync(
        WalkSite site,
        string searchUrl,
        int poolSize,
        Func<string, int, CancellationToken, Task<IReadOnlyList<PageLink>>> loadPageAsync,
        Action<int, Exception> onLaterPageFailed,
        CancellationToken cancellationToken)
    {
        Func<string, int, string>? pageUrlFor = site.PagedSearchUrl;
        List<string> pool = [];
        HashSet<string> canonicalUrls = [];

        for (int pageNumber = 1; ; pageNumber++)
        {
            string pageUrl = pageNumber > 1 && pageUrlFor is not null
                ? pageUrlFor(searchUrl, pageNumber)
                : searchUrl;
            IReadOnlyList<PageLink> anchors;
            try
            {
                anchors = await loadPageAsync(pageUrl, pageNumber, cancellationToken);
            }
            catch (Exception ex) when (pageNumber > 1 && !cancellationToken.IsCancellationRequested)
            {
                onLaterPageFailed(pageNumber, ex);
                break;
            }

            int added = 0;
            foreach (string link in site.CollectDetailLinks(anchors, int.MaxValue))
            {
                if (canonicalUrls.Add(WalkSites.CanonicalDetailUrl(link)))
                {
                    pool.Add(link);
                    added++;
                }
            }

            if (pageUrlFor is null || pool.Count >= poolSize || added == 0)
            {
                break;
            }
        }

        return [.. pool.Take(poolSize)];
    }
}
