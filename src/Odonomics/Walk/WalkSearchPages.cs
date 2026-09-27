namespace Odonomics.Walk;

/// <summary>
/// Collects one search's candidate detail links across as many result pages as the site has and
/// the pool needs. A site whose <see cref="WalkSite.PagedSearchUrl"/> is null (autotrader) has one
/// page per search, so this loads it once and returns what <see cref="WalkSite.CollectDetailLinks"/>
/// finds there. A paged site (carvana renders about 21 cards a page, and its inventory for a model
/// runs to a hundred or more; cars.com asks for a hundred a page and pages the same way) is followed page by page until the pool is full or a page adds no
/// link the pool did not already hold, which also ends the walk of a search whose last page repeats
/// or comes back empty. A site that states how many cars the search matches (see
/// <see cref="WalkSite.MatchCountPattern"/>) has its first page's count taken as the bound on the
/// links every page together contributes, and a page that says the search ran out of exact matches
/// (see <see cref="WalkSite.ExhaustedSearchPattern"/>) contributes nothing and ends the paging, since
/// what follows that notice is similar vehicles the facets never asked for. Only the first page is
/// required: a later page is a bonus, so one that fails to load ends the paging with the links already
/// collected instead of failing the search. A link the caller says it already knows (the ledger holds
/// it) is handed to <c>touchKnownAsync</c> with its card's asking price and badges and never enters the pool, so it
/// spends none of the pool; paging goes on past pages made only of known links, and stops when the pool of
/// new links is full, the stated count is reached, or a page shows nothing not seen before.
/// A site whose later pages need a token from the first (see <see cref="WalkSite.PagingTokenReader"/>) has the first
/// one a page gives passed to every later page's URL.
/// A pool size of <see cref="WalkPairSearches.UnboundedPool"/> is the uncapped walk: the pool never fills,
/// so paging ends only by the stated count, a page that adds nothing, an exhausted search, or a failed page.
/// A bounded pool can instead end the collection with results still unread: it fills while the site has more
/// pages to read, or (only when every link is visited again, <c>revisit</c>, so the pool holds links the ledger
/// already knows) a page holds a link the full pool has no room for. That is reported through <c>onCapped</c>,
/// since the postings the collection never reached must not later be read as cars that left the market. Without
/// <c>revisit</c> a link the pool has no room for is one the ledger does not hold, so every known posting on a
/// page that was read has already been touched from its card and the overflow says nothing about a car that sold.
/// </summary>
public static class WalkSearchPages
{
    /// <summary>The candidate links for <paramref name="searchUrl"/>, in page order, one per
    /// canonical URL and at most <paramref name="poolSize"/>, not counting a link
    /// <paramref name="touchKnownAsync"/> takes (it is given the link's canonical URL, the card's
    /// asking price, or null when the card shows none, and the site's badges the card shows, empty when
    /// it shows none, and returns true for a link it took; it is asked about every distinct link, so a
    /// link it does not take is where a caller keeps the card's badges for the detail visit). <paramref name="loadPageAsync"/> is
    /// given a page's URL and its 1-based number, and does everything a person would on that page
    /// (open it, scroll, dwell, record it) before returning its anchors and text, so the pacing between
    /// pages is the pacing between any two page loads. When a page after the first throws (other than
    /// because <paramref name="cancellationToken"/> was cancelled), <paramref name="onLaterPageFailed"/>
    /// is told its number and the exception and the pool built so far is returned; the pages after it were
    /// never read, so the caller records the pair's coverage as partial (see <see cref="Ledger.RunSources.UnreadKey"/>).
    /// A failure on the first page still propagates, since without it the search has nothing. When a page says the search
    /// ran out of exact matches, <paramref name="onSearchExhausted"/> is told its number. When the
    /// collection stops with the site's results not all read because <paramref name="poolSize"/> was
    /// reached, <paramref name="onCapped"/> is told once. <paramref name="revisit"/> says the pool holds
    /// links the ledger already knows, so a link left out for want of room is a posting the walk did not touch.
    /// <paramref name="maxDistanceMiles"/> is the scenario's own radius, checked against a card's stated
    /// distance for a site with a <see cref="WalkSite.CardDistanceReader"/> (cars.com): a card beyond it, or
    /// one whose text states no distance at all, never enters the pool and is never handed to
    /// <paramref name="touchKnownAsync"/>, and is told to <paramref name="onBeyondRadius"/> or
    /// <paramref name="onNoDistance"/> respectively (see <see cref="WalkSite.CollectDetailCards"/>), once per
    /// canonical URL for the whole search: a padded site that keeps repeating the same out-of-radius car
    /// on every later page is counted for it once, not once per page. A link reported beyond radius or as
    /// stating no distance on one page can still turn up in radius on a later page, off that page's own
    /// text for it: when that happens the earlier report is withdrawn through
    /// <paramref name="onBeyondRadiusWithdrawn"/> or <paramref name="onNoDistanceWithdrawn"/> (whichever one
    /// fired for it) the moment the link is pooled or handed to <paramref name="touchKnownAsync"/>, so a car
    /// the walk ends up keeping is never left counted as skipped. A page made only of such cards, or of
    /// links this pool already holds, adds nothing and ends the paging exactly as an empty page does.
    /// <paramref name="minYearFor"/> and <paramref name="maxMileage"/> are the scenario's own facets, checked
    /// against a card's own stated year and mileage for a site with a <see cref="WalkSite.CardFacetsReader"/>
    /// (carmax, whose search URL carries both facets already but does not actually honor the year range): a
    /// card whose stated year is under the floor (<paramref name="minYearFor"/> takes whether the card's own
    /// model text says "Hybrid", for a hybrid-only-from-year model's base-model card) or whose stated mileage
    /// is over the cap never enters the pool (see <see cref="WalkSite.CollectDetailCards"/>), unlike a
    /// beyond-radius or no-distance card. Unlike one of those, though, it is still handed to
    /// <paramref name="touchKnownAsync"/> first: a known posting is kept current from its card exactly as a
    /// passing one would be, so this walk declining to spend a detail visit on it never reads, on a later
    /// run, as the car having left the market. <paramref name="onBelowYearFloor"/> or
    /// <paramref name="onOverMileageCap"/> is told about it once that touch is done.
    /// A link a page's <see cref="SearchPageContent.UnrenderedHrefs"/> names (see
    /// <see cref="SearchPageCardRenderWait"/>) is dropped from that page before any of that: its card never
    /// rendered within the bounded wait, so it is kept out of <see cref="WalkSite.CollectDetailCards"/> for
    /// that page entirely rather than let the ambiguous wrapper-text walk decide its distance. cars.com's
    /// padded later pages can repeat the same car, so a link named unrendered on one page but read normally
    /// (pooled, touched as known, beyond radius, or no distance) on another is not unrendered at all; only a
    /// link that stays unrendered on every page it appears on, across the whole search, is told to
    /// <paramref name="onUnrendered"/> with its canonical URL, once per canonical URL. A link the ledger
    /// already holds that stays unrendered this way is never handed to <paramref name="touchKnownAsync"/>:
    /// its card was never actually read, so touching it would keep it current for rank without ever
    /// measuring it, and would double-count it as both known from cards and unrendered. Its posting's
    /// LastSeen does not move; the canonical URL <paramref name="onUnrendered"/> is told lets the caller
    /// check whether the ledger already holds it and, if so, record the pair's coverage as partial rather
    /// than let the diff read a car it never actually measured as one that left the market.</summary>
    public static async Task<IReadOnlyList<string>> CollectLinksAsync(
        WalkSite site,
        string searchUrl,
        int poolSize,
        Func<string, decimal?, IReadOnlyDictionary<string, string>, CancellationToken, ValueTask<bool>> touchKnownAsync,
        Func<string, int, CancellationToken, Task<SearchPageContent>> loadPageAsync,
        Action<int, Exception> onLaterPageFailed,
        Action<int> onSearchExhausted,
        Action onCapped,
        CancellationToken cancellationToken,
        bool revisit = false,
        int? maxDistanceMiles = null,
        Action? onBeyondRadius = null,
        Action? onNoDistance = null,
        Action? onBeyondRadiusWithdrawn = null,
        Action? onNoDistanceWithdrawn = null,
        Action<string>? onUnrendered = null,
        Func<bool, int>? minYearFor = null,
        int? maxMileage = null,
        Action? onBelowYearFloor = null,
        Action? onOverMileageCap = null)
    {
        Func<string, int, string?, string>? pageUrlFor = site.PagedSearchUrl;
        string? pagingToken = null;
        List<string> pool = [];
        HashSet<string> canonicalUrls = [];
        Dictionary<string, Action?> reportedOutOfRadius = [];
        HashSet<string> everUnrenderedCanonicalUrls = [];
        int linkBound = int.MaxValue;
        int considered = 0;
        bool leftBehindForWantOfPoolRoom = false;

        void ReportOnce(PageLink card, Action? report, Action? withdraw)
        {
            string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
            if (!canonicalUrls.Contains(canonicalUrl) && !reportedOutOfRadius.ContainsKey(canonicalUrl))
            {
                reportedOutOfRadius[canonicalUrl] = withdraw;
                report?.Invoke();
            }
        }

        for (int pageNumber = 1; ; pageNumber++)
        {
            string pageUrl = pageNumber > 1 && pageUrlFor is not null
                ? pageUrlFor(searchUrl, pageNumber, pagingToken)
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

            pagingToken ??= content.PagingToken;

            if (site.SearchRanOutOfMatches(content.Text))
            {
                onSearchExhausted(pageNumber);
                break;
            }

            if (pageNumber == 1 && site.MatchCountIn(content.Text) is int statedCount)
            {
                linkBound = statedCount;
            }

            IReadOnlyList<PageLink> renderedLinks = content.Links;
            if (content.UnrenderedHrefs is { Count: > 0 } unrenderedHrefs)
            {
                HashSet<string> unrenderedCanonicalUrls = [.. unrenderedHrefs.Select(WalkSites.CanonicalDetailUrl)];
                renderedLinks = [.. content.Links.Where(l => !site.DetailUrlPattern.IsMatch(l.Href) || !unrenderedCanonicalUrls.Contains(WalkSites.CanonicalDetailUrl(l.Href)))];
                everUnrenderedCanonicalUrls.UnionWith(unrenderedCanonicalUrls);
            }

            int added = 0;
            List<PageLink> belowFloorCards = [];
            List<PageLink> overMileageCards = [];
            foreach (PageLink card in site.CollectDetailCards(
                renderedLinks,
                int.MaxValue,
                content.Text,
                maxDistanceMiles,
                card => ReportOnce(card, onBeyondRadius, onBeyondRadiusWithdrawn),
                card => ReportOnce(card, onNoDistance, onNoDistanceWithdrawn),
                minYearFor,
                maxMileage,
                belowFloorCards.Add,
                overMileageCards.Add))
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

                if (reportedOutOfRadius.Remove(canonicalUrl, out Action? withdrawEarlierReport))
                {
                    withdrawEarlierReport?.Invoke();
                }

                considered++;
                added++;
                if (await touchKnownAsync(canonicalUrl, site.ReadCardPrice(card.CardText), site.ReadCardBadges(card.CardText), cancellationToken))
                {
                    continue;
                }

                if (pool.Count < poolSize)
                {
                    pool.Add(card.Href);
                }
                else if (revisit)
                {
                    leftBehindForWantOfPoolRoom = true;
                }
            }

            // A card the facet check dropped never enters the pool, but a known posting is still touched
            // from it exactly as a passing card's would be, so its LastSeen moves with this run and the
            // diff never reads it as having left the market just because this walk will not spend a detail
            // visit on it.
            foreach (PageLink card in belowFloorCards)
            {
                await touchKnownAsync(WalkSites.CanonicalDetailUrl(card.Href), site.ReadCardPrice(card.CardText), site.ReadCardBadges(card.CardText), cancellationToken);
                onBelowYearFloor?.Invoke();
            }

            foreach (PageLink card in overMileageCards)
            {
                await touchKnownAsync(WalkSites.CanonicalDetailUrl(card.Href), site.ReadCardPrice(card.CardText), site.ReadCardBadges(card.CardText), cancellationToken);
                onOverMileageCap?.Invoke();
            }

            // A page that adds nothing, or a stated count already reached, means the site's results are
            // all read even when the pool happens to be full too; only a full pool with more to read is capped.
            if (pageUrlFor is null || considered >= linkBound || added == 0)
            {
                break;
            }

            if (pool.Count >= poolSize)
            {
                leftBehindForWantOfPoolRoom = true;
                break;
            }
        }

        foreach (string canonicalUrl in everUnrenderedCanonicalUrls)
        {
            if (canonicalUrls.Contains(canonicalUrl) || reportedOutOfRadius.ContainsKey(canonicalUrl))
            {
                // Rendered normally on some other page of this search: pooled, touched as known, beyond
                // radius, or no distance already accounts for it, so it is not unrendered after all.
                continue;
            }

            onUnrendered?.Invoke(canonicalUrl);
        }

        if (leftBehindForWantOfPoolRoom)
        {
            onCapped();
        }

        return pool;
    }
}
