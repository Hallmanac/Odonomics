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
    /// <summary>The badges passed to <c>touchKnownAsync</c> for a no-distance card's own protective
    /// touch: its text is never trusted, so it never has badges of its own to offer either.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoCardBadges = new Dictionary<string, string>();

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
    /// one whose text states no distance at all, never enters the pool, and is told to
    /// <paramref name="onBeyondRadius"/> or <paramref name="onNoDistance"/> respectively (see
    /// <see cref="WalkSite.CollectDetailCards"/>), once per canonical URL for the whole search: a padded
    /// site that keeps repeating the same out-of-radius car on every later page is counted for it once, not
    /// once per page. A beyond-radius card is never handed to <paramref name="touchKnownAsync"/>, since its
    /// own text did say where it was; a no-distance card is handed to it anyway, with a null price (its own
    /// text cannot be trusted to be its own, see <see cref="WalkSite.CollectDetailCards"/>'s own
    /// <see cref="WalkSite.CardDistanceReader"/> remarks), so a link the ledger already holds is kept
    /// current from this alone, the same protective reason a below-floor or over-mileage card is touched
    /// (see <paramref name="minYearFor"/> below): its own status may never be knowable from a search page
    /// again, and reading it as gone just because this walk could not confirm it would be worse than
    /// reading its price as unchanged. A link reported beyond radius or as stating no distance on one page
    /// can still turn up in radius on a later page, off that page's own text for it: when that happens the
    /// earlier report is withdrawn through <paramref name="onBeyondRadiusWithdrawn"/> or
    /// <paramref name="onNoDistanceWithdrawn"/> (whichever one fired for it) the moment the link is pooled
    /// or handed to <paramref name="touchKnownAsync"/> the ordinary way, so a car the walk ends up keeping
    /// is never left counted as skipped, and its real price replaces the no-distance touch's own null one.
    /// A page made only of such cards, or of links this pool already holds, adds nothing and ends the
    /// paging exactly as an empty page does.
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
    /// <paramref name="maxPrice"/> is the scenario's own price ceiling, checked against a card's own stated
    /// price plus any shipping or delivery fee that same card states, on every site rather than only one
    /// with a <see cref="WalkSite.CardFacetsReader"/> (see <see cref="WalkSite.CollectDetailCards"/>): a card
    /// over the ceiling never enters the pool either, but is touched from its card first, the same as a
    /// below-floor or over-mileage one, and <paramref name="onOverPriceCeiling"/> is told about it, and it
    /// counts toward the page's own tally of links added, once per canonical URL for the whole search: a
    /// padded site that keeps repeating the same over-ceiling car on every later page has it touched and
    /// counted only the first time, exactly as a beyond-radius card is (see <paramref name="onBeyondRadius"/>
    /// above), so a page made up only of such repeats still reads as adding nothing and ends the paging. A
    /// card whose text states no price at all is unaffected, kept exactly as it always was.
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
    /// than let the diff read a car it never actually measured as one that left the market.
    /// <paramref name="onSearchCoverageKnown"/> is told, once, how many links this search actually
    /// considered and the count its own first page stated (null when it stated none), so a caller with
    /// more than one search for the pair (a hybrid-only-from-year model's hybrid and base-model searches)
    /// can compare the two against each other and decide whether the pair's own coverage is full or
    /// partial (see <see cref="WalkSearchCoverage"/>).
    /// <paramref name="onNoPriceStated"/> is every site's own count of cards that state plainly they have
    /// no price (cargurus's "No Price Listed"), checked ahead of <paramref name="maxPrice"/> (see
    /// <see cref="WalkSite.CollectDetailCards"/>): such a card is never pooled either, but is touched from
    /// its card first, the same as an over-ceiling one, and counts toward the page's own tally of links
    /// added, once per canonical URL for the whole search, the same way an over-ceiling card does.
    /// <paramref name="onCarMaxDealer"/> is cars.com's own count of cards a delivery fee marks as one of
    /// CarMax's own store's listings (see <see cref="WalkSite.CollectDetailCards"/>): also never a
    /// candidate, and, unlike an over-ceiling or no-price card, never touched either, even when the ledger
    /// already holds it, since it is a redundant copy the walk stopped saving outright rather than one this
    /// run simply could not measure. Deduped once per canonical URL for the whole search, the same way an
    /// over-ceiling card is, but unlike an over-ceiling or no-price card it never counts toward a page's own
    /// tally of links added: CarMax's own nationwide inventory hands cars.com a fresh, never-before-seen
    /// listing on page after page, so a page cars.com pads with nothing but new CarMax cards reads as
    /// adding nothing and ends the paging, the same as a page of nothing but repeats. When that page also
    /// held a genuinely new CarMax, beyond-radius, or no-distance card (as opposed to one truly empty of
    /// anything new), <paramref name="onStoppedOnPaddingOnlyPage"/> is told its number: a CarMax or
    /// beyond-radius card is never a candidate this walk would keep no matter what a later page holds, and
    /// a no-distance card's own known posting, if it has one, is kept current the same protective way a
    /// below-floor or over-mileage card's is (see <paramref name="onNoDistance"/> below), so a stop caused
    /// by any of them proves the search's real matches are exhausted the same as an empty page would, and
    /// the pair's own coverage stays full for it. A no-distance card is the one case whose own text never
    /// said whether it was in radius (see <see cref="WalkSite.CollectDetailCards"/>'s own
    /// <see cref="WalkSite.CardDistanceReader"/> remarks), and it can go on to show its own single-card text
    /// several pages after the one that first read it as a wrapper (a real cars.com recording had one
    /// resolve five pages later, and several recorded searches never resolve one at all across the whole
    /// run): rather than leave that genuinely unknowable case to freeze the pair's own coverage every run it
    /// recurs on, a no-distance card whose canonical URL the ledger already holds is touched with no price
    /// of its own the moment it is reported (see <paramref name="onNoDistance"/> below), so its LastSeen
    /// moves with this run and it is never read, in the diff, as having left the market just because its
    /// own text could not be trusted. A brand-new no-distance card the ledger has never seen has nothing to
    /// protect this way, and is simply not a candidate this run; a later run whose padding stops sooner, or
    /// whose card finally resolves, can still pick it up.</summary>
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
        Action? onOverMileageCap = null,
        int? maxPrice = null,
        Action? onOverPriceCeiling = null,
        Action<int, int?>? onSearchCoverageKnown = null,
        Action? onNoPriceStated = null,
        Action? onCarMaxDealer = null,
        Action<int>? onStoppedOnPaddingOnlyPage = null)
    {
        Func<string, int, string?, string>? pageUrlFor = site.PagedSearchUrl;
        string? pagingToken = null;
        List<string> pool = [];
        HashSet<string> canonicalUrls = [];
        Dictionary<string, Action?> reportedOutOfRadius = [];
        HashSet<string> reportedOverPriceCeiling = [];
        HashSet<string> reportedNoPriceStated = [];
        HashSet<string> reportedCarMaxDealer = [];
        HashSet<string> everUnrenderedCanonicalUrls = [];
        int linkBound = int.MaxValue;
        int considered = 0;
        bool leftBehindForWantOfPoolRoom = false;

        // Set while processing the page currently in hand, and read only at that same page's own
        // "did this page add anything" check below: true when this page reported a CarMax dealer card,
        // a beyond-radius card, or a no-distance card for the first time in this whole search (a repeat
        // of one already reported on an earlier page leaves it false). A page that adds nothing new of
        // any kind at all (every link a repeat, or the page genuinely empty) leaves this false too, the
        // same "nothing to learn from stopping here" case the pre-existing empty-page stop already
        // covered; onStoppedOnPaddingOnlyPage is only ever told about the other case, where the page
        // held brand-new links this run chose not to treat as added.
        bool pageHadNewExcludedActivity = false;

        void ReportOnce(PageLink card, Action? report, Action? withdraw)
        {
            string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
            if (!canonicalUrls.Contains(canonicalUrl) && !reportedOutOfRadius.ContainsKey(canonicalUrl))
            {
                reportedOutOfRadius[canonicalUrl] = withdraw;
                pageHadNewExcludedActivity = true;
                report?.Invoke();
            }
        }

        for (int pageNumber = 1; ; pageNumber++)
        {
            pageHadNewExcludedActivity = false;

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
            List<PageLink> overPriceCeilingCards = [];
            List<PageLink> noPriceCards = [];
            List<PageLink> carMaxDealerCards = [];
            List<PageLink> noDistanceCards = [];
            foreach (PageLink card in site.CollectDetailCards(
                renderedLinks,
                int.MaxValue,
                content.Text,
                maxDistanceMiles,
                card => ReportOnce(card, onBeyondRadius, onBeyondRadiusWithdrawn),
                card =>
                {
                    ReportOnce(card, onNoDistance, onNoDistanceWithdrawn);
                    noDistanceCards.Add(card);
                },
                minYearFor,
                maxMileage,
                belowFloorCards.Add,
                overMileageCards.Add,
                maxPrice,
                overPriceCeilingCards.Add,
                noPriceCards.Add,
                carMaxDealerCards.Add))
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

                if (reportedOutOfRadius.Remove(canonicalUrl, out Action? earlierWithdraw))
                {
                    earlierWithdraw?.Invoke();
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

            // A no-distance card's own text never says whether it is in radius (an ambiguous multi-card
            // wrapper, see WalkSite.CollectDetailCards's own CardDistanceReader remarks), and some never
            // resolve at all across a whole recorded search, so its own text is never trusted for a price
            // either. But a known posting behind one is still touched here, with a null price, the same
            // protective reason a below-floor or over-mileage card's is above: its LastSeen moves with this
            // run, so a card this walk can never resolve never reads, in the diff, as the car having left
            // the market, and the pair's own coverage never needs marking partial for it (see
            // onStoppedOnPaddingOnlyPage below). A card that goes on to show its own distance on a later
            // page is pooled or resolved against the ledger the ordinary way, which overwrites this touch's
            // own null price with a real one.
            foreach (PageLink card in noDistanceCards)
            {
                await touchKnownAsync(WalkSites.CanonicalDetailUrl(card.Href), null, NoCardBadges, cancellationToken);
            }

            // An over-ceiling card is still one of the page's exact matches (see WalkSites.CollectDetailCards,
            // which now bounds the price check to those before this ever sees a padding card), so it counts
            // the same way a pooled one does: without this, a URL-paged site's page made up entirely of cars
            // over the ceiling would read as adding nothing and end the paging, leaving every later page,
            // cheaper cars included, unread. But a padded page can repeat the same over-ceiling card the
            // search already reported (cars.com's later pages can repeat the same far, expensive cards on
            // every page), so, exactly as ReportOnce does for a beyond-radius or no-distance card, a
            // canonical URL already pooled or known, or already reported over the ceiling earlier in this
            // search, counts toward neither added nor considered again: otherwise a page made up only of
            // such repeats would still read as adding something and cars.com's only stop rule (a page that
            // adds nothing) would never fire.
            foreach (PageLink card in overPriceCeilingCards)
            {
                string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
                if (canonicalUrls.Contains(canonicalUrl) || !reportedOverPriceCeiling.Add(canonicalUrl))
                {
                    continue;
                }

                considered++;
                added++;
                await touchKnownAsync(canonicalUrl, site.ReadCardPrice(card.CardText), site.ReadCardBadges(card.CardText), cancellationToken);
                onOverPriceCeiling?.Invoke();
            }

            // A card stating plainly it has no price is still one of the page's exact matches, counted the
            // same way an over-ceiling one is, and for the same reason: a page made up only of such cards
            // (or repeats of one a padded later page keeps showing) must still read as adding something, or
            // paging would stop before the site's real results are read.
            foreach (PageLink card in noPriceCards)
            {
                string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
                if (canonicalUrls.Contains(canonicalUrl) || !reportedNoPriceStated.Add(canonicalUrl))
                {
                    continue;
                }

                considered++;
                added++;
                await touchKnownAsync(canonicalUrl, site.ReadCardPrice(card.CardText), site.ReadCardBadges(card.CardText), cancellationToken);
                onNoPriceStated?.Invoke();
            }

            // Unlike an over-ceiling or no-price card, a CarMax card is never counted toward this page's
            // own "added" tally, whether it is new or a repeat: CarMax's own nationwide inventory hands
            // cars.com a fresh, never-before-seen listing URL on page after page (about two a page,
            // walk 20260928-184522's Prius pair), so counting a new one toward "added" the way a genuine
            // organic match is would keep paging going for as long as CarMax's own inventory holds out,
            // dozens of pages past the pair's last real match. It is still deduped and still counted
            // toward `considered`, and still never touched: it is not a car this walk simply couldn't
            // price or afford, it is a redundant copy of a listing CarMax's own walk already covers,
            // which the walk stopped saving outright (see WalkSite.CollectDetailCards). A posting
            // already on the ledger from before this exclusion existed is meant to stop being kept
            // current here, not only stop being visited, so touchKnownAsync is never even asked about
            // one of these.
            foreach (PageLink card in carMaxDealerCards)
            {
                string canonicalUrl = WalkSites.CanonicalDetailUrl(card.Href);
                if (canonicalUrls.Contains(canonicalUrl) || !reportedCarMaxDealer.Add(canonicalUrl))
                {
                    continue;
                }

                considered++;
                pageHadNewExcludedActivity = true;
                onCarMaxDealer?.Invoke();
            }

            // A page that adds nothing, or a stated count already reached, means the site's results are
            // all read even when the pool happens to be full too; only a full pool with more to read is capped.
            if (pageUrlFor is null || considered >= linkBound || added == 0)
            {
                // The stop itself is the same "a page that adds nothing ends it" rule as an empty or
                // all-repeat page; what sets this apart is that the page was not actually empty of new
                // information, it just held only CarMax cards, beyond-radius cards, or no-distance cards
                // this run declines to chase further (see pageHadNewExcludedActivity above and
                // WalkSite.CollectDetailCards). A CarMax or beyond-radius card is never a candidate this
                // walk would keep regardless of what a later page holds, and a no-distance card's own known
                // posting, if it has one, was already kept current above the moment it was reported, so the
                // pair's own coverage stays full for this stop the same way it does for an ordinary
                // empty-page one; onStoppedOnPaddingOnlyPage is only for the console line explaining why.
                if (added == 0 && pageUrlFor is not null && considered < linkBound && pageHadNewExcludedActivity)
                {
                    onStoppedOnPaddingOnlyPage?.Invoke(pageNumber);
                }

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

        onSearchCoverageKnown?.Invoke(considered, linkBound == int.MaxValue ? null : linkBound);

        return pool;
    }
}
