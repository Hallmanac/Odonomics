using System.Text.Json;
using System.Text.RegularExpressions;
using Odonomics.Ledger;
using Odonomics.Sources;

namespace Odonomics.Walk;

/// <summary>One walk target's search-URL builder, detail-link pattern, and how much the walk
/// should over-fetch candidate links. <see cref="DetailLinkOverfetchMultiplier"/> stays above 1
/// even for a site whose search URL isolates the requested model, since a page rejected as
/// <see cref="Odonomics.Walk.DetailPageOutcome.Repeat"/> or <see cref="Odonomics.Walk.DetailPageOutcome.NotMatching"/>
/// (the latter now also expected for cars.com's base-model bucket on a hybrid-only-from-year
/// model, see <see cref="ListingQuery.HybridOnlyFromModelYear"/>) needs a spare
/// link to replace it with rather than shortening the pair; see docs/walk.md for the
/// full reasoning. <paramref name="BuildSearchUrls"/> takes the scenario-derived
/// <see cref="ListingQuery"/> for one model and returns the one or more search URLs the walk visits
/// for it, in order, sharing the per-pair cap (see <see cref="WalkPairSearches"/>); the query carries
/// make, model, zip, radius, whether the scenario marks
/// this model's base model as hybrid-only from some year onward (see
/// <see cref="Odonomics.Domain.Scenario.HybridOnlyFromModelYear"/>), and the minimum model year and
/// maximum mileage, which every site takes as search facets so the per-pair cap is spent only on
/// cars the scenario can rank. <paramref name="FallbackDealerName"/>
/// is the dealer a posting is stamped with when its detail page names none, for a site where the
/// site itself is the seller (carvana); null for a marketplace whose pages carry the dealer's own
/// name or none at all. <paramref name="PagedSearchUrl"/> is null for a site whose search is one page,
/// and for one whose results run to more pages (cars.com and carvana by a <c>page=N</c> parameter,
/// autotrader by a <c>firstRecord=N</c> record offset) it turns a search URL, a 1-based page number, and the
/// paging token the search's first page gave (null for a site with none) into
/// that page's URL (see <see cref="WalkSearchPages"/>). <paramref name="MatchCountPattern"/> is for a site whose search page states
/// how many listings match ("13 Matches") and keeps filling the page with cards for other models and
/// years after them (autotrader), or states one ("16 cars") and may show a few more results than it counts
/// (carvana): its first group is that count, and <see cref="CollectDetailLinks"/> trusts the count over the
/// cards. A paged site's first page count also bounds the links all its pages contribute together.
/// <paramref name="ResultCardLinkPattern"/> says which of a counted page's links are its result cards (autotrader's <c>clickType=listing</c>), since
/// the count does not include the sponsored card that sits first in page order. <paramref name="PrivateSellerPagePattern"/>
/// is for a site whose detail page marks a private seller in its own text (autotrader's
/// "Sample S (Private Seller)" line): a page it matches is stored with
/// <see cref="WalkSites.PrivateSellerDealerName"/> and no location, whatever name the extraction
/// read off it. <paramref name="ShippingFeeReader"/> reads the one-time shipping fee off a detail page's
/// text for a site that prints one (carvana); null for a site that does not, whose postings store no
/// fee. <paramref name="ExhaustedSearchPattern"/> is for a paged site whose later pages, once the
/// search's real matches run out, are padded with similar vehicles (carvana's "No exact matches"):
/// a page whose text matches it contributes no links and ends paging (see
/// <see cref="WalkSearchPages"/>). <paramref name="CardPriceReader"/> reads a result card's asking price
/// off the card's own text, so a listing the ledger already knows can be kept current from the search
/// page alone (see <see cref="ReadCardPrice"/>); null for a site whose card shape has not been confirmed
/// from a recorded page, whose known listings are then seen again without a price. <paramref name="CardContainerSelector"/>
/// is a CSS selector for the element that is one result card, for a site where the nearest ancestor holding
/// a dollar amount (the default, see <see cref="SearchPageLinks"/>) is not the card. <paramref name="CardAmountPattern"/>
/// overrides, as a JavaScript regular expression source, what counts as the card's own price marker during that
/// ancestor climb, for a site whose card prints its price some other way than a dollar sign followed by a digit
/// (autotrader, whose price is bare digits beside "See payment"; see <see cref="SearchPageLinks.CardAmountPattern"/>
/// and <see cref="WalkSites.Autotrader"/>); null for a site whose card the default dollar-sign test already finds.
/// <paramref name="CardAmountMarkerIsInnerToCard"/> says the amount marker the ancestor climb stops at sits inside
/// the real card rather than at its outer edge (autotrader: the price/"See payment" line sits below the card's own
/// top-of-card badge, such as "Price Drop", and above its dealer footer, so the first matching ancestor is missing
/// both; see <see cref="WalkSites.Autotrader"/> and <see cref="SearchPageLinks.CardScript"/>). When set, the climb
/// keeps widening past that first match, one parent at a time, for as long as the parent's own text still carries
/// only the one amount match the card itself accounts for; the moment a parent's text carries more than one (the
/// results list wrapping several cards, each with its own price line), the widening stops and the last ancestor
/// under that bound is the card. False for a site whose first matching ancestor already is the whole card.
/// <paramref name="CardBadgeReader"/>
/// reads the site's own badges off a card's text as display-only posting attributes (see
/// <see cref="ReadCardBadges"/>); null for a site whose card shape has not been confirmed.
/// <paramref name="PickupReader"/>
/// reads the pickup option (where, and at what fee) off a detail page's text for a site that offers one beside
/// delivery (carvana); null for a site that does not, whose postings store no pickup facts.
/// <paramref name="LazyDetailBlockMarker"/> is a line of text a detail page prints only once the block the walk
/// needs has rendered, for a site that renders it lazily, after a scroll (carvana's "Pickup and Delivery"): the
/// walk keeps scrolling the page until the text carries it (see <see cref="DetailPageScroll"/>).
/// <paramref name="SoldPagePattern"/> and <paramref name="NoPricePagePattern"/> are for a site whose detail page
/// says in its own text that the car has sold (autotrader's "has already found a new home") or lists no price
/// (autotrader's "Contact Dealer For Price"); a page either one matches is dropped before extraction as
/// <see cref="DetailPageOutcome.Sold"/> or <see cref="DetailPageOutcome.NoPriceListed"/> (see
/// <see cref="ReadsAsSold"/>). Null for a site whose wording has not been confirmed from a recorded page, whose
/// such pages are then dropped for whatever the extraction makes of them.
/// <paramref name="FeeStatementReader"/> reads what a detail page says about the dealer fees behind its price
/// (see <see cref="FeeStatements"/>); null for a site whose dealers do not add fees to the price (carvana),
/// whose postings then store no fee posture.
/// <paramref name="CardFeeReader"/> reads what taking the car home costs off a result card's own text (carmax, which
/// prints it on every card, see <see cref="CarMaxCards"/>); null for a site whose fee, if it prints one, is read off the detail page.
/// <paramref name="DetailHtmlVinReader"/> reads the VIN off a detail page's HTML for a site whose visible text does
/// not print it (carmax, see <see cref="CarMaxVin"/>); null for a site whose text does. <paramref name="LoadMoreControlPattern"/>
/// matches the label of the control a site's search page has to be pressed at to show more cards ("Show 25 matches" or "Load more"),
/// for a site that loads its cards that way instead of by page number (see <see cref="SearchPageLoadMore"/>).
/// <paramref name="SponsoredLinkPattern"/> matches a detail link a search page carries for a sponsored card (cargurus's
/// <c>sponsoredType=PRIORITY</c>, <c>FEATURED</c>, and <c>HIGHLIGHT</c>, everything but <c>NONE</c>): such a link is never a candidate, since a sponsored card ignores the search's facets and
/// is not one of the counted results. <paramref name="PagingTokenReader"/> reads, off the HTML of a search's first page, the
/// value the URLs of its later pages have to carry for them to line up with it (cargurus's <c>pageAlignment</c>), and
/// <paramref name="PagedSearchUrl"/> then takes it as its third argument. <paramref name="CardFeeStatementReader"/> reads what a
/// result card says about the fees behind its price (cargurus, see <see cref="FeeStatements.ReadCarGurusCard"/>), for a site whose card says
/// it and whose detail page does not agree with it; when it is set, the card's statement is the one a posting is stored with.
/// <paramref name="AskingPriceFromCard"/> says the card's price is the one to store, for a site whose card shows what the buyer pays
/// delivered (cargurus, whose detail page also lists the dealer's price at its lot, shipping not in it, in a store-transfer breakdown).
/// <paramref name="DetailDealerReader"/> reads the dealer a detail page names, ahead of the extraction, for a site whose
/// dealer is either one of many stores under a single name that the extraction does not reliably tell from the site
/// itself (carmax, see <see cref="CarMaxStores"/>), or a site with no fallback dealer whose own text names both parts
/// deterministically enough to read without the extraction (cargurus, see <see cref="CarGurusDealer"/>); null for a
/// site whose dealer the extraction reads unaided.
/// <paramref name="DetailTitleModelReader"/> reads the model off a detail page's own title line ("2026 Toyota Camry SE Sedan
/// 4D"), for a site whose extraction can leave the model blank on a page that prints it there (every site sets this; see
/// <see cref="ReadTitleModel"/>); null for a site whose extraction is taken as it comes.
/// <paramref name="CardDistanceReader"/> reads the distance in miles a result card
/// states from the search's zip (cars.com, see <see cref="CarsComCards.ReadDistanceMiles"/>), for a site whose search URL
/// carries a radius the site does not actually enforce; null for a site whose radius the search itself holds to, whose
/// cards are then never checked against it. <paramref name="WaitsForRenderedCards"/> says this site's cards render
/// lazily enough that link collection has to wait for them (cars.com, see <see cref="SearchPageCardRenderWait"/>): a
/// card still empty when the page first loads leaves <see cref="SearchPageLinks.CardScript"/>'s dollar-ancestor walk
/// nothing of its own to stop at, so it climbs to the results list and can read a neighboring card's own stated
/// distance as this card's. False for a site whose cards are there by the time the walk reads them.
/// <paramref name="CardFacetsReader"/> reads a result card's own stated model year, mileage, and whether its model
/// text says "Hybrid" (carmax, see <see cref="CarMaxCards.ReadVehicleFacets"/>), for a site whose search URL carries
/// the scenario's minimum year and maximum mileage as facets but does not actually honor them: a card whose stated
/// year or mileage already fails those facets never enters the pool a detail visit could come from (see
/// <see cref="CollectDetailCards"/>), the same as a beyond-radius or no-distance card, but unlike one of those a
/// known posting behind it is still kept current from its card (see <see cref="WalkSearchPages.CollectLinksAsync"/>)
/// rather than left to read as gone for want of a visit this walk was never going to spend on it. Null for a site
/// whose own search facets are trusted as they come, whose cards are then never checked against them.
/// <paramref name="NoPriceCardPattern"/> matches a card that states plainly it has no price (cargurus's "No Price
/// Listed"), checked ahead of the price-ceiling check so such a card is never pooled and never mistaken for one
/// whose price this walk simply couldn't read: it is instead told to <c>onNoPriceStated</c> (see
/// <see cref="CollectDetailCards"/> and <see cref="WalkSearchPages.CollectLinksAsync"/>), the same way a
/// below-floor or over-ceiling card is, with a known posting behind it still touched current from its card. A
/// card like this can still carry a shipping-only line ("Price includes $1,498 shipping") that names no price of
/// its own, which <see cref="ReadCardPrice"/> already never reads as one; null for a site with no such
/// statement. <paramref name="CarMaxCardReader"/> tells a card that is really one of CarMax's own
/// nationwide-inventory listings, carried on cars.com's own search, from an ordinary dealer's card that
/// also happens to state a delivery fee (cars.com, see <see cref="CarsComCards.ReadsAsCarMax"/>): checked
/// instead of <see cref="CardFeeReader"/> returning non-null for the <see cref="SkipsCarMaxDealer"/> check
/// below, since an ordinary distant dealer's own delivery fee satisfies that too. Null for a site with no
/// such distinction to draw.</summary>
public sealed record WalkSite(
    string Name,
    Func<ListingQuery, IReadOnlyList<string>> BuildSearchUrls,
    Regex DetailUrlPattern,
    int DetailLinkOverfetchMultiplier = 1,
    string? FallbackDealerName = null,
    Regex? SkippedCardTitlePattern = null,
    Func<string, int, string?, string>? PagedSearchUrl = null,
    Regex? MatchCountPattern = null,
    Regex? PrivateSellerPagePattern = null,
    Regex? ResultCardLinkPattern = null,
    Func<string, decimal?>? ShippingFeeReader = null,
    Regex? ExhaustedSearchPattern = null,
    Func<string, decimal?>? CardPriceReader = null,
    string? CardContainerSelector = null,
    string? CardAmountPattern = null,
    bool CardAmountMarkerIsInnerToCard = false,
    Func<string, IReadOnlyDictionary<string, string>>? CardBadgeReader = null,
    Func<string, PickupOption?>? PickupReader = null,
    string? LazyDetailBlockMarker = null,
    Regex? SoldPagePattern = null,
    Regex? NoPricePagePattern = null,
    Func<string, FeeStatement>? FeeStatementReader = null,
    Func<string, CardFee?>? CardFeeReader = null,
    Func<string, string?>? DetailHtmlVinReader = null,
    Regex? LoadMoreControlPattern = null,
    Regex? SponsoredLinkPattern = null,
    Func<string, string?>? PagingTokenReader = null,
    Func<string, FeeStatement>? CardFeeStatementReader = null,
    bool AskingPriceFromCard = false,
    Func<string, ResolvedDealer?>? DetailDealerReader = null,
    Func<string, string?>? DetailAvailabilityReader = null,
    Func<string, string?, string?, int?, string?>? DetailTitleModelReader = null,
    Func<string, int?>? CardDistanceReader = null,
    bool WaitsForRenderedCards = false,
    Func<string, CardVehicleFacets>? CardFacetsReader = null,
    Regex? NoPriceCardPattern = null,
    bool SkipsCarMaxDealer = false,
    Func<string, bool>? CarMaxCardReader = null)
{
    /// <summary>The candidate detail links on a search page, in page order, at most
    /// <paramref name="poolSize"/> of them: every link this site's <see cref="DetailUrlPattern"/>
    /// matches, one per canonical URL, minus any listing whose card title matches
    /// <see cref="SkippedCardTitlePattern"/> (cars.com mixes new-car cards into a used search, and
    /// the scenario can never rank one). A listing is skipped when any of its anchors carries such
    /// a title, since a card links its photo and its title separately and only the title anchor
    /// has text. A skipped card never enters the pool, so it never counts against the per-pair cap.
    /// When this site has a <see cref="MatchCountPattern"/> and <paramref name="searchPageText"/> states
    /// a count, the page's cards are trusted only that far: a count of zero yields no links at all, and
    /// a positive count yields at most that many, since the matching listings come first in page order
    /// and everything after them is filler (other models, other years, new cars, listings outside the
    /// search radius) that the search facets never asked for. The exception is a sponsored card, which
    /// sits first in page order, ignores the search facets, and is not one of the counted matches, so
    /// when this site has a <see cref="ResultCardLinkPattern"/> and a count is stated, only the links
    /// that pattern matches are drawn on, and the cap is spent on real results rather than an ad (if
    /// no link matches the pattern, the page is taken in page order as before). A page that states no
    /// count is taken as it comes.</summary>
    public IReadOnlyList<string> CollectDetailLinks(IReadOnlyList<PageLink> links, int poolSize, string? searchPageText = null) =>
        [.. CollectDetailCards(links, poolSize, searchPageText).Select(l => l.Href)];

    /// <summary>The same links <see cref="CollectDetailLinks"/> returns, each with its result card's
    /// text: when a listing is linked more than once (a card's photo and its title), the first link is
    /// kept, carrying the first card text any of its links has. When this site has a
    /// <see cref="CardDistanceReader"/> and <paramref name="maxDistanceMiles"/> is given, a card whose
    /// reader finds a distance over that bound never enters the pool either, and
    /// <paramref name="onBeyondRadius"/> is told it (cars.com's search URL carries a radius the site does
    /// not itself enforce, so a card far outside it still turns up). A card whose text states no distance
    /// at all, or states more than one (empty text, a neighboring card's own text mistaken for this card's,
    /// or one of the site's own nationwide recommendation links padding a later page, any of which can end
    /// up carrying a multi-card wrapper's text rather than a single card's), is kept out the same way, but
    /// is told to <paramref name="onNoDistance"/> instead, so a reader can tell "measured and too far" from
    /// "never measured" apart. Neither ever counts against
    /// <paramref name="poolSize"/> or enters the pool, the same as a title
    /// <see cref="SkippedCardTitlePattern"/> drops. When this site has a <see cref="CardFacetsReader"/>, a
    /// card whose own text states a model year under <paramref name="minYearFor"/> (given whether that
    /// card's own model text says "Hybrid", for a hybrid-only-from-year model's base-model card) is told to
    /// <paramref name="onBelowYearFloor"/> instead of ever being pooled, and one whose stated mileage is over
    /// <paramref name="maxMileage"/> is told to <paramref name="onOverMileageCap"/> the same way; a card whose
    /// text states neither is unaffected. Unlike a beyond-radius or no-distance card, though, this method does
    /// not decide whether a known posting behind it gets touched from the card first: that is
    /// <see cref="WalkSearchPages.CollectLinksAsync"/>'s own job, done before it counts the card as skipped for
    /// its year or mileage. Checked ahead of the radius check above, so a card dropped for its year or mileage
    /// is never also reported beyond radius or as stating none. Checked last, after both of those, when
    /// <paramref name="maxPrice"/> is given, a card whose own stated price (see <see cref="ReadCardPrice"/>),
    /// plus any shipping or delivery fee that same card states (see <see cref="ReadCardFee"/>; not added when
    /// <see cref="AskingPriceFromCard"/> is set, since such a card's price already has its shipping fee inside
    /// it, cargurus's "Price includes $462 shipping"), comes to more than that ceiling, is told to
    /// <paramref name="onOverPriceCeiling"/> instead of ever being pooled: the scenario's own price ceiling,
    /// checked on every site alike rather than only one with a <see cref="CardFacetsReader"/>. A card whose
    /// text states no price at all is unaffected, so an unread price is never mistaken for one over the
    /// ceiling. Checked after the radius check rather than ahead of it, unlike the facet checks: a card a
    /// site's own ambiguous multi-card wrapper text already dropped as beyond radius or stating no distance
    /// (cars.com: see <paramref name="onNoDistance"/> above) never has a neighbouring card's price read as
    /// its own and mistaken for this card being over or under the ceiling. When this site has a
    /// <see cref="NoPriceCardPattern"/>, checked ahead of the price-ceiling check (and regardless of whether
    /// <paramref name="maxPrice"/> is given at all), a card its own text matches (cargurus's "No Price Listed") is
    /// told to <paramref name="onNoPriceStated"/> instead of ever being pooled, so a card cargurus itself says has
    /// no price never spends a detail visit finding that out the slow way, and its "Price includes $1,498
    /// shipping" line is never read as if it were the price. Checked right after the radius check, when this
    /// site has <see cref="SkipsCarMaxDealer"/> set and a <see cref="CarMaxCardReader"/>: a card that reader
    /// reads as one of CarMax's own nationwide-inventory listings carried on cars.com's own search (its dealer
    /// line prints one of CarMax's own store names right above a "$249 delivery to Orlando, FL (14 mi)" line,
    /// which reads delivery <em>to</em> the search's own zip; an ordinary distant dealer's card can charge its
    /// own delivery fee too, but reads "delivery from" the seller's own city instead, and is left alone, see
    /// <see cref="CarsComCards.ReadsAsCarMax"/>), so it is told to
    /// <paramref name="onCarMaxDealer"/> instead of ever being pooled, the same as a beyond-radius or
    /// no-distance card: CarMax is walked nationwide by its own site already, and cars.com's delivery offer
    /// does not actually hold for a car CarMax will only sell "Only at" its one store (see
    /// <see cref="IsCarMaxDealer"/>). Unlike a below-floor, over-mileage, over-ceiling, or no-price card, a
    /// known posting behind a card dropped this way is never touched from it either: it is a redundant copy
    /// the walk stopped saving outright, not one this walk simply couldn't measure, so keeping it current
    /// would tell the diff a car left cars.com the moment CarMax's own copy of it goes stale, when the truth
    /// is the opposite. Checked before the price-ceiling and no-price checks, so a CarMax card that also
    /// happens to be over budget or state no price is still reported as CarMax, not one of those.</summary>
    public IReadOnlyList<PageLink> CollectDetailCards(
        IReadOnlyList<PageLink> links,
        int poolSize,
        string? searchPageText = null,
        int? maxDistanceMiles = null,
        Action<PageLink>? onBeyondRadius = null,
        Action<PageLink>? onNoDistance = null,
        Func<bool, int>? minYearFor = null,
        int? maxMileage = null,
        Action<PageLink>? onBelowYearFloor = null,
        Action<PageLink>? onOverMileageCap = null,
        int? maxPrice = null,
        Action<PageLink>? onOverPriceCeiling = null,
        Action<PageLink>? onNoPriceStated = null,
        Action<PageLink>? onCarMaxDealer = null)
    {
        int? statedCount = MatchCountIn(searchPageText);
        if (statedCount is int matchCount)
        {
            poolSize = Math.Min(poolSize, matchCount);
        }

        List<PageLink> detailLinks = [.. links.Where(l => DetailUrlPattern.IsMatch(l.Href) && !IsSponsored(l))];
        HashSet<string> skipped = SkippedCardTitlePattern is null
            ? []
            : [.. detailLinks
                .Where(l => SkippedCardTitlePattern.IsMatch(l.Text))
                .Select(l => WalkSites.CanonicalDetailUrl(l.Href))];

        if (ResultCardLinkPattern is not null && statedCount is not null)
        {
            List<PageLink> resultCards = [.. detailLinks.Where(l => ResultCardLinkPattern.IsMatch(l.Href))];
            detailLinks = resultCards.Count > 0 ? resultCards : detailLinks;
        }

        List<PageLink> candidates =
        [
            .. detailLinks
                .GroupBy(l => WalkSites.CanonicalDetailUrl(l.Href))
                .Where(g => !skipped.Contains(g.Key))
                .Select(g => g.First() with { CardText = g.Select(l => l.CardText).FirstOrDefault(t => t.Length > 0) ?? "" })
        ];

        // A stated count already narrowed poolSize above; taken here, ahead of the facet, radius, and price
        // checks below, so a card one of them drops can never free up room for a padding card beyond the
        // site's own exact matches to backfill into (a carvana page can state "16 cars" and still render 20,
        // the last 4 being other models the search facets never asked for, and letting the price ceiling
        // check run over all 20 would spend a detail visit on those 4 once enough of the real 16 dropped out).
        candidates = [.. candidates.Take(poolSize)];

        if (CardFacetsReader is not null && (minYearFor is not null || maxMileage is not null))
        {
            List<PageLink> withinFacets = [];
            foreach (PageLink card in candidates)
            {
                CardVehicleFacets facets = CardFacetsReader(card.CardText);
                if (minYearFor is not null && facets.Year is int year && year < minYearFor(facets.ModelNamesHybrid))
                {
                    onBelowYearFloor?.Invoke(card);
                    continue;
                }

                if (maxMileage is int cap && facets.Mileage is int mileage && mileage > cap)
                {
                    onOverMileageCap?.Invoke(card);
                    continue;
                }

                withinFacets.Add(card);
            }

            candidates = withinFacets;
        }

        if (CardDistanceReader is not null && maxDistanceMiles is int radius)
        {
            List<PageLink> inRadius = [];
            foreach (PageLink card in candidates)
            {
                int? distance = CardDistanceReader(card.CardText);
                if (distance is null)
                {
                    onNoDistance?.Invoke(card);
                }
                else if (distance > radius)
                {
                    onBeyondRadius?.Invoke(card);
                }
                else
                {
                    inRadius.Add(card);
                }
            }

            candidates = inRadius;
        }

        // A CarMax listing carried on cars.com's own search prints a delivery fee on its card ("$249
        // delivery to Orlando, FL (14 mi)"), or names CarMax as the dealer, or both; an ordinary cars.com
        // dealer's card that also charges its own delivery fee reads "delivery from" instead (lesson
        // 4a2cbaa3), which is why this is CarMaxCardReader, not CardFeeReader returning non-null: that
        // would also catch a genuine distant dealer's own delivery-fee card as if it were CarMax's. Checked
        // here, before any detail page is ever opened, so a CarMax card never spends a detail visit or an
        // LLM extraction on every run just to be dropped after the fact the way WalkCommand.VisitLinkAsync's
        // own IsCarMaxDealer check still does for the rare card this reader misses.
        if (SkipsCarMaxDealer && CarMaxCardReader is not null)
        {
            List<PageLink> notCarMax = [];
            foreach (PageLink card in candidates)
            {
                if (CarMaxCardReader(card.CardText))
                {
                    onCarMaxDealer?.Invoke(card);
                    continue;
                }

                notCarMax.Add(card);
            }

            candidates = notCarMax;
        }

        // Checked ahead of the price ceiling below, and regardless of whether one is even set: a card
        // that states plainly it has no price (cargurus's "No Price Listed") is dropped here so it is
        // never pooled and never mistaken, by the ceiling check below, for a card whose price this walk
        // simply never found a way to read.
        if (NoPriceCardPattern is not null)
        {
            List<PageLink> priced = [];
            foreach (PageLink card in candidates)
            {
                if (NoPriceCardPattern.IsMatch(card.CardText))
                {
                    onNoPriceStated?.Invoke(card);
                    continue;
                }

                priced.Add(card);
            }

            candidates = priced;
        }

        // Checked last, after the radius (and no-distance) check above: a card whose text is an
        // ambiguous multi-card wrapper (see CardDistanceReader's own remarks) reads as stating no
        // distance and is already dropped by then, so its price, read from a neighbouring card's
        // text, never reaches this check and never sends a known posting behind it to touchKnownAsync
        // with another car's figures.
        if (maxPrice is int priceCeiling)
        {
            List<PageLink> withinCeiling = [];
            foreach (PageLink card in candidates)
            {
                if (ReadCardPrice(card.CardText) is decimal cardPrice)
                {
                    decimal shippingFee = AskingPriceFromCard ? 0m : ReadCardFee(card.CardText)?.ShippingFee ?? 0m;
                    if (cardPrice + shippingFee > priceCeiling)
                    {
                        onOverPriceCeiling?.Invoke(card);
                        continue;
                    }
                }

                withinCeiling.Add(card);
            }

            candidates = withinCeiling;
        }

        return [.. candidates.Take(poolSize)];
    }

    /// <summary>Whether <paramref name="link"/> is the link of a sponsored card (see <see cref="SponsoredLinkPattern"/>). Only that link
    /// is set aside: a listing that is sponsored on one card and an ordinary result on another is still a candidate through the
    /// ordinary one.</summary>
    private bool IsSponsored(PageLink link) => SponsoredLinkPattern is not null && SponsoredLinkPattern.IsMatch(link.Href);

    /// <summary>The number of listings <paramref name="searchPageText"/> states this search matches
    /// (see <see cref="MatchCountPattern"/>), or null when this site has no pattern or the text states
    /// no count.</summary>
    public int? MatchCountIn(string? searchPageText)
    {
        Match match = MatchCountPattern is null || searchPageText is null
            ? Match.Empty
            : MatchCountPattern.Match(searchPageText);
        return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out int count)
            ? count
            : null;
    }

    /// <summary>Whether <paramref name="searchPageText"/> says this search has run out of exact
    /// matches (see <see cref="ExhaustedSearchPattern"/>), so whatever cards follow are padding.</summary>
    public bool SearchRanOutOfMatches(string? searchPageText) =>
        ExhaustedSearchPattern is not null && searchPageText is not null && ExhaustedSearchPattern.IsMatch(searchPageText);

    /// <summary>Whether <paramref name="pageText"/> says the car has sold (see <see cref="SoldPagePattern"/>).
    /// Checked ahead of <see cref="ReadsAsNoPrice"/>, since a sold page carries other cars' cards and
    /// their prices or prompts.</summary>
    public bool ReadsAsSold(string pageText) => SoldPagePattern is not null && SoldPagePattern.IsMatch(pageText);

    /// <summary>Whether <paramref name="pageText"/> lists no price, only a prompt to contact the dealer
    /// (see <see cref="NoPricePagePattern"/>).</summary>
    public bool ReadsAsNoPrice(string pageText) => NoPricePagePattern is not null && NoPricePagePattern.IsMatch(pageText);

    /// <summary>The shipping fee a detail page shows on top of its asking price, or null when this
    /// site prints none or the page carries none.</summary>
    public decimal? ReadShippingFee(string pageText) => ShippingFeeReader?.Invoke(pageText);

    /// <summary>The pickup option a detail page offers, or null when this site offers none or the
    /// page's block did not render.</summary>
    public PickupOption? ReadPickup(string pageText) => PickupReader?.Invoke(pageText);

    /// <summary>What a detail page says about its dealer fees, or null when this site has no
    /// <see cref="FeeStatementReader"/>, so a posting from it keeps a null fee posture ("never
    /// read") and is never mistaken for an unknown one.</summary>
    public FeeStatement? ReadFeeStatement(string pageText) => FeeStatementReader?.Invoke(pageText);

    /// <summary>The asking price a result card shows, read off <paramref name="cardText"/> by this
    /// site's <see cref="CardPriceReader"/>, or null when the site has none or the card shows no price
    /// it can read.</summary>
    public decimal? ReadCardPrice(string cardText) => CardPriceReader?.Invoke(cardText);

    /// <summary>The badges a result card shows, read off <paramref name="cardText"/> by this site's
    /// <see cref="CardBadgeReader"/> and keyed by <see cref="Odonomics.Ledger.PostingAttributeNames"/>: empty when
    /// the site has no reader or the card shows no badge. They are stored for display and never
    /// scored.</summary>
    public IReadOnlyDictionary<string, string> ReadCardBadges(string cardText) =>
        CardBadgeReader?.Invoke(cardText) ?? new Dictionary<string, string>();

    /// <summary>What a result card says about the fees behind its price, read off <paramref name="cardText"/> by this
    /// site's <see cref="CardFeeStatementReader"/>, or null when the site has none.</summary>
    public FeeStatement? ReadCardFeeStatement(string cardText) => CardFeeStatementReader?.Invoke(cardText);

    /// <summary>The asking price to store for a detail page whose extraction read <paramref name="extractedPrice"/> and whose
    /// link's result card said <paramref name="cardText"/> (null when the search pages showed none). A site that stores
    /// the card's price (see <see cref="AskingPriceFromCard"/>) gets the card's, and null when there is no card or it
    /// shows no price, so a price read off the detail page, a different figure, is never stored in its place; any other
    /// site's asking price is the extraction's.</summary>
    public decimal? AskingPriceOf(decimal? extractedPrice, string? cardText) =>
        AskingPriceFromCard
            ? cardText is null ? null : ReadCardPrice(cardText)
            : extractedPrice;

    /// <summary>What a link's fees are stated to be: its result card's statement when this site's cards make one (see
    /// <see cref="CardFeeStatementReader"/>), otherwise what its detail page says (see <see cref="FeeStatementReader"/>),
    /// or null when neither reader applies.</summary>
    public FeeStatement? FeeStatementOf(string? cardText, string detailPageText) =>
        (cardText is null ? null : ReadCardFeeStatement(cardText)) ?? ReadFeeStatement(detailPageText);

    /// <summary>The paging token a search's first page gives its later pages' URLs, read off <paramref name="html"/> by this
    /// site's <see cref="PagingTokenReader"/>, or null when the site has none or the page gives none.</summary>
    public string? ReadPagingToken(string html) => PagingTokenReader?.Invoke(html);

    /// <summary>The fee a result card shows for getting the car home, read off <paramref name="cardText"/>
    /// by this site's <see cref="CardFeeReader"/>, or null when the site has none or the card shows none.</summary>
    public CardFee? ReadCardFee(string cardText) => CardFeeReader?.Invoke(cardText);

    /// <summary>The VIN a detail page's HTML carries, read by this site's <see cref="DetailHtmlVinReader"/>,
    /// or null when the site has none or the HTML carries none.</summary>
    public string? ReadDetailHtmlVin(string html) => DetailHtmlVinReader?.Invoke(html);

    /// <summary>The model to store for a detail page whose extraction returned <paramref name="extractedModel"/>: that model
    /// when it is not blank, otherwise the one the page's own title line prints (see <see cref="DetailTitleModelReader"/>)
    /// for the page's <paramref name="make"/>, <paramref name="trim"/>, and <paramref name="year"/>, or the blank model
    /// back when this site has no reader or the title names none.</summary>
    public string? ResolveModel(string? extractedModel, string? make, string? trim, int? year, string pageText) =>
        string.IsNullOrWhiteSpace(extractedModel)
            ? DetailTitleModelReader?.Invoke(pageText, make, trim, year) ?? extractedModel
            : extractedModel;

    /// <summary>The dealer name to store for a page whose extraction returned
    /// <paramref name="extractedDealerName"/>: that name (trimmed) when the page gave one, such as
    /// a carvana hub ("Carvana Winder"), otherwise <see cref="FallbackDealerName"/>, which is null
    /// for a site with none.</summary>
    public string? ResolveDealerName(string? extractedDealerName) =>
        string.IsNullOrWhiteSpace(extractedDealerName)
            ? FallbackDealerName
            : extractedDealerName.Trim();

    /// <summary>Whether a detail page's own text says the posting cannot be bought like an ordinary
    /// listing right now (CarMax's "Reserved at" or "Coming to" header, see
    /// <see cref="CarMaxStores.ReadAvailability"/>), read by this site's
    /// <see cref="DetailAvailabilityReader"/>, or null when the site has none or the page's header
    /// says nothing of the kind. This does change whether the posting is priced: `odo rank` and `odo
    /// show` skip a posting flagged this way when picking what to price the vehicle from, and a
    /// vehicle with no other purchasable, priced posting is excluded outright (see
    /// <see cref="Ledger.VehiclePricing.OnlyReservedOrInTransit"/>). Unlike a card badge, though, a
    /// null here beside a header that named a recognised store is proof the earlier state ended, not
    /// silence about it, so the caller clears the stored value rather than leaving it (see
    /// <see cref="Ledger.ListingCandidate.AttributesToClear"/>).</summary>
    public string? ReadDetailAvailability(string pageText) => DetailAvailabilityReader?.Invoke(pageText);

    /// <summary>The dealer a candidate from this site is stored with, given what extraction returned.
    /// The fallback applies when the page named no dealer, or when the name it named is the fallback
    /// itself (the model echoing the site's own name off a footer is the page naming no hub, not a
    /// hub). Then the location is dropped and <see cref="ResolvedDealer.IsFallback"/> is set: the
    /// fallback is one dealer, "Carvana" with no location, rather than a row per pickup city the
    /// page happened to print, and the upsert uses the flag so a sighting that named no hub never
    /// replaces a link to a more specific dealer an earlier sighting established. A page that reads
    /// as a private seller's (see <see cref="PrivateSellerPagePattern"/>, checked against
    /// <paramref name="pageText"/>) is stored with <see cref="WalkSites.PrivateSellerDealerName"/> and
    /// no location instead: the extraction would read a person's name and city off it, and a person is
    /// not a dealer row. That name is a fact the page states, not a fallback, so it is not flagged as one.
    /// A store the site's <see cref="DetailDealerReader"/> finds in <paramref name="pageText"/> is the dealer, whatever
    /// extraction returned, and is not a fallback either. When the reader names only a name or only a
    /// location (a delivery-only CarGurus page whose "Dealer" block gives a name but whose only city is
    /// in a street address the reader does not parse, or a page whose "Dealer" block names nobody but
    /// whose description names one in free text the reader does not look at), the half it left null is
    /// filled from the extraction rather than dropped: taking the reader's result whole even when it is
    /// half empty would otherwise send the posting to a dealer row missing the half the extraction did
    /// read, which is exactly the "partial reading replaces a better one" this method exists to avoid.</summary>
    public ResolvedDealer ResolveDealer(string? extractedDealerName, string? extractedDealerLocation, string? pageText = null)
    {
        if (ReadsAsPrivateSeller(pageText))
        {
            return new ResolvedDealer(WalkSites.PrivateSellerDealerName, null, IsFallback: false);
        }

        if (pageText is not null && DetailDealerReader?.Invoke(pageText) is { } pageDealer)
        {
            return pageDealer.Name is not null && pageDealer.Location is not null
                ? pageDealer
                : pageDealer with
                {
                    Name = pageDealer.Name ?? ResolveDealerName(extractedDealerName),
                    Location = pageDealer.Location ?? extractedDealerLocation,
                };
        }

        return FallbackDealerName is not null && NamesNoDealerBeyondTheSite(extractedDealerName)
            ? new ResolvedDealer(FallbackDealerName, null, IsFallback: true)
            : new ResolvedDealer(ResolveDealerName(extractedDealerName), extractedDealerLocation, IsFallback: false);
    }

    private bool ReadsAsPrivateSeller(string? pageText) =>
        PrivateSellerPagePattern is not null && pageText is not null && PrivateSellerPagePattern.IsMatch(pageText);

    private bool NamesNoDealerBeyondTheSite(string? extractedDealerName) =>
        string.IsNullOrWhiteSpace(extractedDealerName)
        || DealerNormalizer.Normalize(extractedDealerName) == DealerNormalizer.Normalize(FallbackDealerName);

    /// <summary>True when <paramref name="dealerName"/> (the dealer <see cref="ResolveDealer"/> just
    /// resolved) names one of CarMax's own stores, on a site whose <see cref="SkipsCarMaxDealer"/>
    /// flag is set: cars.com carries CarMax's whole nationwide inventory on its own search results
    /// (a "$1,999 delivery to Orlando, FL (14 mi)" card for a car thousands of miles off), which
    /// CarMax's own walk already covers end to end, and cars.com's delivery offer does not actually
    /// hold for a car CarMax will only sell "Only at" its one store. cars.com has no
    /// <see cref="DetailDealerReader"/> of its own, so its dealer is whatever extraction read off
    /// the page verbatim, the same full "CarMax &lt;city&gt;" name <see cref="CarMaxStores.Read"/>
    /// resolves for CarMax's own pages ("CarMax Tri-Cities Kennewick", "CarMax Norco"), so matching
    /// on the "CarMax" prefix alone is enough: no curated store list is needed the way
    /// <see cref="CarMaxStores.OnlyAtStoreName"/>'s distance check needs one, since this only has to
    /// tell a CarMax store from an ordinary dealer, not measure how far away it is. False for a site
    /// with the flag unset, or when <paramref name="dealerName"/> is null or does not start with
    /// "CarMax".</summary>
    public bool IsCarMaxDealer(string? dealerName) =>
        SkipsCarMaxDealer && dealerName is not null && dealerName.StartsWith(WalkSites.CarMaxDealerName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>One anchor read off a search page: its resolved href, its visible text, which on a
/// cars.com card link is the card's title ("Used 2024 Toyota Corolla LE"), and the text of the result
/// card the anchor sits in (empty when the pull found none, see <see cref="SearchPageLinks"/>).</summary>
public readonly record struct PageLink(string Href, string Text, string CardText = "");

/// <summary>One loaded search page: its anchors, its visible text for a site that states its
/// match count there (see <see cref="WalkSite.MatchCountPattern"/>), the paging token its HTML gave, for a site whose
/// later pages have to carry one (see <see cref="WalkSite.PagingTokenReader"/>), and, for a site whose cards render
/// lazily (see <see cref="WalkSite.WaitsForRenderedCards"/>), the hrefs of the cards still empty once the bounded
/// render wait gave up on them (see <see cref="SearchPageCardRenderWait"/>); null or empty for a site with nothing
/// to report.</summary>
public readonly record struct SearchPageContent(IReadOnlyList<PageLink> Links, string? Text = null, string? PagingToken = null, IReadOnlyList<string>? UnrenderedHrefs = null);

/// <summary>The dealer name and location a walked candidate is stored with, and whether the name is
/// the site's fallback rather than one the page gave.</summary>
public readonly record struct ResolvedDealer(string? Name, string? Location, bool IsFallback);

/// <summary>Search-URL shapes and detail-link patterns for the walk targets: cars.com and carvana, whose
/// hybrid facets the rest of this comment is about, autotrader (see <see cref="Autotrader"/>), carmax (see <see cref="CarMax"/>), and cargurus (see <see cref="CarGurus"/>). The spike's
/// docs/spike-findings.md recorded both sites as having no working hybrid facet, but that recording
/// doesn't hold up against the spike's own day-one capture: the cars.com response to a
/// "toyota-corolla_hybrid" query (spike/recorded/cars.com/day1/Toyota-Corolla_Hybrid-search.html)
/// is a complete, non-degraded results page, yet its own selected_search_filters on that request
/// still resolved back to the base "toyota-corolla", so the earlier conclusion rested on a
/// request whose facet just didn't apply that day, not on a bot-defended or otherwise broken
/// response. Brian has since confirmed the same underscored value does apply when a person ticks
/// the hybrid facet by hand: his warmed Edge profile built and used that exact URL (project home
/// notes/run-session-2026-09-22.md, "Facet URLs from Brian"). What actually differs between the
/// two requests (cookies, an A/B bucket, some other session state) is unconfirmed; the
/// operator-built URL is what the walk now trusts. cars.com's model facet lowercases the model
/// name and replaces every run of non-alphanumeric characters with an underscore, after the make
/// and a hyphen ("Toyota Corolla Hybrid" -> models[]=toyota-corolla_hybrid, "Honda CR-V Hybrid" ->
/// models[]=honda-cr_v_hybrid), and carvana has no separate hybrid model at all; it filters its
/// base-model search by fuel type (parentModels: "Corolla" plus fuelTypes: ["Hybrid"]) instead.
/// cars.com's hybrid facet is a distinct model bucket, not every hybrid instance of the base
/// model: a scenario's HybridOnlyFromModelYear rule exists because a base model can go
/// hybrid-only from some year on without cars.com ever moving those listings into the hybrid
/// bucket (Toyota's 2025+ Camry is filed under plain "camry", not "camry_hybrid"; the operator's
/// own recorded run confirms it: the base-model query's search.txt, under the walk data
/// directory outside this repo at
/// "walks/cars.com/20260922-184927/camry-hybrid/search.txt", lists gas-titled Camrys with zero
/// case-insensitive "hybrid" occurrences). For a model with that rule set, cars.com's builder
/// below returns two searches, the hybrid facet and the base-model facet: the hybrid facet reaches
/// the pre-cutover model years actually filed there while the base-model facet reaches the
/// post-cutover years cars.com never moved into the hybrid bucket. ListingQuery.MatchesExtractedVehicle
/// is what then accepts a base-titled candidate at or after the hybrid-only year and rejects a
/// genuinely-gas one below it. Carvana needs no equivalent second query: its fuelTypes filter
/// matches each listing's actual fuel type, not its title text, so a hybrid-only-from-year
/// model's newer listings already come back correctly under the base-model query it always
/// uses.
///
/// <para>Those two facets cannot share one search URL, though. cars.com takes a single year_min
/// for the whole request, so one URL carrying both facets has to use the scenario's minimum year
/// (2018 for the Camry Hybrid) for the base model too, and every gas Camry from 2018 through
/// the hybrid-only year comes back, crowds the results, and is then rejected by
/// ListingQuery.MatchesExtractedVehicle after the per-pair cap has been spent on it (walk run 4
/// saw 14 of 24 drops be exactly that, with the real hybrid titles sitting at positions 24 and 25
/// of 35). So for a hybrid-only-from-year model the cars.com builder returns two URLs: the hybrid
/// facet from the scenario's minimum year, then the base-model facet from
/// <see cref="ListingQuery.HybridOnlyFromModelYear"/> itself (or the scenario's minimum year if
/// that is later), so it can only return the years that are actually hybrid. The two share the cap, with any share one cannot spend passing to the other (see <see cref="WalkPairSearches"/>).</para></summary>
public static class WalkSites
{
    public static string Slugify(string value) => value.ToLowerInvariant().Replace(" ", "-");

    /// <summary>Strips a detail link down to scheme, host, and path, dropping every query
    /// parameter and any fragment. Every walk site appends a per-search-session or per-click
    /// parameter (cars.com's "sid", carried on every "/vehicledetail/" href on the page, and
    /// autotrader's "clickType"), cars.com additionally emits more than one
    /// query-string variant of the same card's link ("?sid=…" and
    /// "?openLeadForm=true&amp;sid=…"), and autotrader links a card a second time with a
    /// "#purchaseConfidence" fragment. Canonicalizing before the walk dedupes those variants
    /// into one candidate and gives <c>ListingCandidate.Url</c> a value that is stable across
    /// runs, so <c>LedgerUpsertService</c>'s (Vin, Source, Url) lookup can actually recognize the
    /// same posting again instead of minting a new one every time the session id changes.</summary>
    public static string CanonicalDetailUrl(string href) => new Uri(href).GetLeftPart(UriPartial.Path);

    /// <summary>cars.com's model-facet value for a model name: lowercased, with every run of
    /// characters that isn't a letter or digit collapsed to a single underscore ("Corolla Hybrid"
    /// -> "corolla_hybrid", "Corolla Cross" -> "corolla_cross", "CR-V Hybrid" -> "cr_v_hybrid"),
    /// matching the exact value the site's own browser puts in models[] when that model is ticked
    /// in the left-column facet.</summary>
    private static string ModelFacetWords(string model) => Regex.Replace(model.ToLowerInvariant(), "[^a-z0-9]+", "_");

    /// <summary>The scenario model with a trailing " Hybrid" removed, for carvana's parentModels
    /// facet: carvana has no separate hybrid model, so the base model plus fuelTypes=Hybrid is
    /// the filtering mechanism (see the class remarks).</summary>
    private static string BaseModelName(string model)
    {
        int hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return hybridIndex >= 0 ? model[..hybridIndex] : model;
    }

    private static bool IsHybridVariant(string model) => model.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    /// <summary>Base64url-encodes carvana's cvnaid JSON filter payload, unpadded (no trailing
    /// "=") to match the value carvana's own browser puts on the URL.</summary>
    private static string EncodeCvnaid(string json)
    {
        string base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    /// <summary>The condition badge a title line can carry ahead of its model year ("Used 2020 Toyota
    /// Corolla LE", "Certified 2026 Toyota Corolla SE FWD"), shared between <see cref="DetailTitleLine"/>
    /// and <see cref="ReadTitleModel"/> so the two agree on what counts as one.</summary>
    private const string TitleConditionWord = @"(?:(?:New|Used|Certified|Price Drop)\s+)?";

    /// <summary>The first line of a page that opens a vehicle title: a run of exactly four digits (a
    /// model year), optionally behind a condition word, followed by more text. Only the first such line
    /// in the whole page counts, whichever car it turns out to name: a mismatch against the year and make
    /// actually asked for (see <see cref="ReadTitleModel"/>) means the page's own title disagrees, not
    /// that some other, later mention of the right year and make (a similar-vehicles card further down
    /// the page) should be searched out instead.</summary>
    private static readonly Regex DetailTitleLine = new(
        $@"^[ \t]*{TitleConditionWord}\d{{4}}[ \t]+\S.*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Reads the model off a detail page's own title line ("2026 Toyota Camry SE Sedan 4D"), for
    /// a page whose extraction left the model blank even though the page states it. Every site's title
    /// line takes the form "&lt;year&gt; &lt;make&gt; &lt;model&gt; &lt;trim&gt;", optionally behind a
    /// condition word; when the extraction's own <paramref name="trim"/> is given, the model is what sits
    /// between the make and that trim, found as a whole word so a trim that happens to be a substring of
    /// the model or body style (an "SE" trim inside "SE Sedan 4D") does not cut short. Without a trim to
    /// bound against, or on a site that renders the trim on the line below the title instead (carvana,
    /// carmax, whose title line then has nothing after the model to bound against anyway), the whole
    /// remainder of the line is the model. Null when the make or year is unknown, the page's first title
    /// line names some other year or make, or what follows the make on it is empty.</summary>
    private static string? ReadTitleModel(string pageText, string? make, string? trim, int? year)
    {
        if (string.IsNullOrWhiteSpace(make) || year is not int titleYear)
        {
            return null;
        }

        Match titleLine = DetailTitleLine.Match(pageText);
        if (!titleLine.Success)
        {
            return null;
        }

        Match match = Regex.Match(
            titleLine.Value.Trim(),
            $@"^{TitleConditionWord}{titleYear}\s+{Regex.Escape(make.Trim())}\s+(?<rest>\S.*)$",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        string rest = match.Groups["rest"].Value.Trim();
        if (!string.IsNullOrWhiteSpace(trim))
        {
            Match beforeTrim = Regex.Match(rest, $@"^(?<model>.*?)\b{Regex.Escape(trim.Trim())}\b", RegexOptions.IgnoreCase);
            if (beforeTrim.Success)
            {
                rest = beforeTrim.Groups["model"].Value;
            }
        }

        string model = Regex.Replace(rest, @"\s+", " ").Trim();
        return model.Length > 0 ? model : null;
    }

    public static readonly WalkSite CarsCom = new(
        "cars.com",
        query =>
        {
            string makeSlug = Slugify(query.Make);
            string hybridModelSlug = $"{makeSlug}-{ModelFacetWords(query.Model)}";
            string SearchUrl(string modelSlug, int yearMin) =>
                $"https://www.cars.com/shopping/results/?stock_type=used&makes[]={makeSlug}" +
                $"&models[]={modelSlug}&zip={query.Zip}&maximum_distance={query.RadiusMiles}" +
                $"&year_min={yearMin}&mileage_max={query.MaxMileage}";

            return query.HybridOnlyFromModelYear is int hybridOnlyYear
                ? [
                    SearchUrl(hybridModelSlug, query.YearMin),
                    SearchUrl($"{makeSlug}-{ModelFacetWords(BaseModelName(query.Model))}", Math.Max(hybridOnlyYear, query.YearMin)),
                ]
                : [SearchUrl(hybridModelSlug, query.YearMin)];
        },
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        SkippedCardTitlePattern: new Regex(@"^\s*New\s", RegexOptions.IgnoreCase),
        // A cars.com card reads its asking price first, then a price-drop amount when it has one, then
        // mileage, then the "Used <year> ..." title, so the first dollar amount is the price.
        CardPriceReader: CardPrices.FirstDollarAmount,
        // cars.com pages with a plain page=N on the same URL; the site ignores a page_size parameter, so
        // the search URL carries none. How many cards one page holds varies by search, down to a handful
        // for one with few real matches (see the SkipsCarMaxDealer remark below): it is the pair's own
        // count of real matches plus padding, not a fixed page size.
        PagedSearchUrl: (searchUrl, pageNumber, _) => $"{searchUrl}&page={pageNumber}",
        // A CarMax listing carried on cars.com's own search prints a delivery fee on its card ("$249
        // delivery to Orlando, FL (14 mi)"); an ordinary distant dealer's card can print its own delivery
        // fee too ("$150 delivery from Palmetto Bay, FL"), so telling the two apart is CarMaxCardReader's
        // job below, not this reader's (lesson 4a2cbaa3).
        CardFeeReader: CarsComCards.ReadFee,
        CarMaxCardReader: CarsComCards.ReadsAsCarMax,
        CardBadgeReader: CardBadges.CarsCom,
        FeeStatementReader: FeeStatements.ReadCarsCom,
        // cars.com's maximum_distance query parameter doesn't bound the results it actually returns
        // (a padded page keeps handing back cars hundreds of miles off), so every card's own stated
        // distance is checked against the scenario's radius at link collection instead.
        CardDistanceReader: CarsComCards.ReadDistanceMiles,
        DetailTitleModelReader: ReadTitleModel,
        // A fuse-card can still be empty when the page first loads; reading it then risks the
        // dollar-ancestor walk landing on the results list and misreading a neighbor's distance as
        // this card's (walk run 20260927-113258).
        WaitsForRenderedCards: true,
        // CarMax is walked nationwide by its own site, and cars.com's own delivery offer does not
        // hold for an "Only at" CarMax car anyway, so a CarMax dealer on cars.com is always skipped
        // rather than saved as a second, redundant posting (see WalkCommand.VisitLinkAsync and
        // Ledger.VehiclePricing).
        //
        // Walk run 20260928-184522's eleven page=N loads in a row that each rendered only three to seven
        // cards were not a page cut short by a scroll too shallow to mount the rest: the same run's own
        // fixed ScrollInStepsAsync mounted 32 cards on the Corolla Hybrid pair's page 1 and 36 on the
        // Prius pair's page 1 on 20260928-134242, and the pre-regression 20260922-184927 Insight walk
        // already read only 9 cards on its own page 1. A thin cars.com page is the pair's own true count
        // of real matches; the pages after it repeat a handful of sponsored and CarMax delivery cards plus
        // about two new CarMax cards each and are padding, not more organic matches (lesson 9dec3616), so
        // adding more scroll here would not raise a page's own card count and does not belong here. What
        // was actually starving a page's organic count towards "the 2 or so" was the CarMax check above
        // still reading CardFeeReader's non-null return as "is CarMax": an ordinary dealer far from the
        // search zip who also charges its own delivery fee ("$150 delivery from Palmetto Bay, FL") was
        // wrongly dropped as a redundant CarMax copy under that reading (lesson 4a2cbaa3), which
        // CarMaxCardReader above now corrects (see CarsComCards.ReadsAsCarMax).
        SkipsCarMaxDealer: true);

    /// <summary>What carvana's own name is stored as when a detail page names no hub. A carvana
    /// detail page usually prints no dealer at all (the car ships from a hub the page never names),
    /// and the model reading such a page sometimes returns "Carvana" itself and sometimes nothing,
    /// so every carvana posting would otherwise show a dealer only by that coin flip.</summary>
    public const string CarvanaDealerName = "Carvana";

    public static readonly WalkSite Carvana = new(
        "carvana",
        query =>
        {
            var makes = new[] { new { name = query.Make, parentModels = new[] { new { name = BaseModelName(query.Model) } } } };
            var year = new { min = query.YearMin };
            var mileage = new { max = query.MaxMileage };
            object filters = IsHybridVariant(query.Model)
                ? new { filters = new { makes, fuelTypes = new[] { "Hybrid" }, year, mileage } }
                : new { filters = new { makes, year, mileage } };
            return [$"https://www.carvana.com/cars/filters?zip={query.Zip}&cvnaid={EncodeCvnaid(JsonSerializer.Serialize(filters))}"];
        },
        new Regex("/vehicle/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        FallbackDealerName: CarvanaDealerName,
        // Carvana renders about 21 cards a page and pages with a plain page=N on the same filters URL.
        PagedSearchUrl: (searchUrl, pageNumber, _) => $"{searchUrl}&page={pageNumber}",
        // A search page states "16 cars" and may show a few more results than that; once the exact
        // matches run out the site says "No exact matches" and pads the page with similar vehicles
        // (other models), so the count bounds the links and that phrase ends the paging.
        MatchCountPattern: new Regex(@"(?<![\d,])(\d[\d,]*)\s+cars?\b"),
        ShippingFeeReader: CarvanaShipping.Read,
        ExhaustedSearchPattern: new Regex("No exact matches", RegexOptions.IgnoreCase),
        // A carvana card names its asking price after "Current price:", and a marked-down one then
        // prints "Original price: was $..." as well, so only the amount after "Current price:" counts.
        CardPriceReader: CardPrices.CarvanaCurrentPrice,
        // A carvana card states its own shipping fee ("Free shipping" or "$690 shipping") the same way its
        // detail page does, ahead of the "Get it <day>" line.
        CardFeeReader: CarvanaShipping.ReadCardFee,
        CardBadgeReader: CardBadges.Carvana,
        // The delivery block also carries the pickup option, and renders only after the page has been
        // scrolled about a third of the way down.
        PickupReader: CarvanaPickup.Read,
        LazyDetailBlockMarker: CarvanaPickup.BlockHeading,
        DetailTitleModelReader: ReadTitleModel);

    /// <summary>What a private seller's listing is stored as: one dealer row for every private seller,
    /// with no location, so no individual's name or city enters the ledger and
    /// <c>odo dealer grade</c> has one row to skip rather than a person to look up on CarEdge (see
    /// <see cref="PrivateSellerDealers"/>).</summary>
    public const string PrivateSellerDealerName = "Private seller";

    /// <summary>autotrader's own override of <see cref="SearchPageLinks.CardAmountPattern"/>: the default
    /// dollar sign, or "See payment" on its own (every real listing card's price marker, priced or not).
    /// Matching on "See payment" alone, rather than requiring digits before it, is what lets the climb
    /// stop at an unpriced card ("Contact Dealer For Price" / "See payment", no digits and no dollar
    /// sign anywhere near it, recorded walk run 20260926-121057, prius/search.txt lines 388-400): a
    /// marker that required digits first never matched that card's own text, so the climb passed it by
    /// and landed on the results list, picking up a neighboring card's price and badges instead. Kept as
    /// an alternative to the dollar sign rather than a replacement, since a "New ... MSRP$" recommendation
    /// card still prints one; that card is never a candidate either way (its link carries no
    /// clickType=listing, see <see cref="WalkSite.ResultCardLinkPattern"/>).</summary>
    private const string AutotraderCardAmountPattern = SearchPageLinks.CardAmountPattern + @"|See payment";

    /// <summary>autotrader's zip, radius, minimum year, maximum mileage, and hybrid facets. Both dealers and
    /// private sellers list there, so the URL carries no sellerTypes parameter (sellerTypes=d and
    /// sellerTypes=p narrow to one or the other). autotrader has one model per base model, so a hybrid
    /// variant ("Corolla Hybrid") searches its base model ("corolla") with fuelTypeGroup=HYB, while a
    /// model that is hybrid by name ("Prius", "Insight") has no such facet to add. A model slug it does
    /// not know is not an error: the site silently returns every listing for the make, which is why the
    /// slug is the base model and never "corolla-hybrid". The site rewrites the URL to
    /// /cars-for-sale/&lt;make&gt;/&lt;model&gt;/&lt;city&gt;-&lt;state&gt;?... and honors each parameter. A search
    /// page states "N Matches" and then fills the rest of the page with cards for other years, other
    /// models, new cars and listings outside the radius, so the count is read before the cards are
    /// trusted (see <see cref="WalkSite.CollectDetailLinks"/>): the count covers only the cards the page
    /// marks <c>clickType=listing</c>, not the sponsored top card (<c>clickType=alpha</c>), which ignores the
    /// search facets. A detail link is
    /// /cars-for-sale/vehicle/&lt;digits&gt;, sometimes followed by a query string and a fragment such as
    /// #purchaseConfidence, both of which <see cref="CanonicalDetailUrl"/> strips. Its card shows the price
    /// as bare digits ("19,394", no dollar sign) immediately before "See payment" (recorded run
    /// 20260927-162013, insight/search.txt lines 42-43), and an unpriced card prints "See payment" with
    /// no digits before it at all (recorded run 20260926-121057, prius/search.txt lines 388-400), so
    /// <see cref="WalkSite.CardAmountPattern"/> tells <see cref="SearchPageLinks.CardScript"/>'s ancestor
    /// climb to accept "See payment" on its own beside the default dollar sign, and
    /// <see cref="WalkSite.CardPriceReader"/> separately reads the bare-digit figure off the card when
    /// there is one (see <see cref="CardPrices.AutotraderCardPrice"/>). Before this, every real card's
    /// own dollar-sign test failed and read as no card at all; only a "New ... MSRP$" recommendation card
    /// and a "Consider Buying New" one, both of which print a dollar sign, ever carried card text, and
    /// neither is ever a candidate anyway (its link carries no clickType=listing).
    ///
    /// <para>autotrader pages its results by record offset: a live search fetched directly from
    /// autotrader.com on 2026-09-27 (a 365-match Corolla search, zip 32801, 50 miles) showed the same
    /// 25 <c>clickType=listing</c> cards on its first page every time, and requesting the identical URL
    /// with <c>&amp;firstRecord=25</c> appended returned a second page of 25 more, with zero overlap in
    /// listing ids against the first: <see cref="WalkSite.PagedSearchUrl"/> below reproduces that
    /// offset directly, 25 records per page, rather than a page-number parameter like cars.com's and
    /// carvana's <c>page=N</c>. Before this, autotrader had no <see cref="WalkSite.PagedSearchUrl"/> at
    /// all, so <see cref="WalkSearchPages.CollectLinksAsync"/> read only the first page of any search,
    /// however many the stated count promised (walk run 20260927-162013's corolla-hybrid search stated
    /// 39 Matches and recorded only the 25 on its first page).</para>
    ///
    /// <para>The same live fetch also settled <see cref="WalkSite.CardAmountMarkerIsInnerToCard"/>: a
    /// real listing card's DOM, from the anchor outward, is a "title-info" div (year/make/model/trim),
    /// inside an "inventory-listing-body" div that adds the price and "See payment" (the first ancestor
    /// <see cref="SearchPageLinks.CardScript"/>'s climb used to stop at), inside an "item-card" div that
    /// adds the dealer's name, distance, phone, and "Check Availability" or "Online Paperwork", inside one
    /// more wrapping div that adds a top-of-card badge such as "Price Drop" or "Newly Listed" when the
    /// card has one. Stopping at the first match (the old behavior) read a card missing both its top
    /// badge and its dealer footer (PR #79 review found this from the recorded card 778466582 against
    /// walks/autotrader/20260927-162013/insight/search.txt lines 225-238, before this live DOM confirmed
    /// why). One ancestor further out than that wrapping div is the results list holding every other card
    /// on the page (and, on a page with a sponsored card, its "Sponsored by ..." text too), so the climb
    /// must widen past the price marker but stop short of that shared list; <see cref="CardAmountMarkerIsInnerToCard"/>
    /// does exactly that.</para></summary>
    public static readonly WalkSite Autotrader = new(
        "autotrader",
        query =>
        {
            string url = $"https://www.autotrader.com/cars-for-sale/used-cars/{Slugify(query.Make)}/{Slugify(BaseModelName(query.Model))}" +
                $"?zip={query.Zip}&searchRadius={query.RadiusMiles}&startYear={query.YearMin}&maxMileage={query.MaxMileage}&sortBy=relevance";
            return [IsHybridVariant(query.Model) ? $"{url}&fuelTypeGroup=HYB" : url];
        },
        new Regex(@"/cars-for-sale/vehicle/\d+", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        // 25 clickType=listing cards per page, offset by record count rather than a page number (see
        // the class remarks above for the live fetch that confirmed both the count and the offset).
        PagedSearchUrl: (searchUrl, pageNumber, _) => $"{searchUrl}&firstRecord={(pageNumber - 1) * 25}",
        MatchCountPattern: new Regex(@"(?<![\d,])(\d[\d,]*)\s+Match(?:es)?\b"),
        PrivateSellerPagePattern: new Regex(@"\(Private Seller\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        ResultCardLinkPattern: new Regex(@"[?&]clickType=listing(?:&|$)"),
        // A sold car's page swaps its own listing for "It looks like this Toyota Corolla has already
        // found a new home." above a set of similar cars, and a page with no asking price prints
        // "Contact Dealer For Price" as a line of its own where the price would be (recorded in walk
        // run 20260926-121057, corolla-hybrid/detail-21 and prius/detail-15). The price prompt has to be
        // a whole line, so a longer sentence that merely contains it never reads as a page with none, and
        // it has to come before the "View similar vehicles" line that ends the page's header: further
        // down, the dealer's other cars are listed as cards, and an unpriced card prints the same prompt
        // on a page whose own car is priced.
        SoldPagePattern: new Regex(@"has already found a new home", RegexOptions.IgnoreCase),
        NoPricePagePattern: new Regex(
            @"\A(?:(?!^\s*View similar vehicles\s*$)[\s\S])*^\s*Contact Dealer For Price\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline),
        CardPriceReader: CardPrices.AutotraderCardPrice,
        CardAmountPattern: AutotraderCardAmountPattern,
        CardAmountMarkerIsInnerToCard: true,
        CardBadgeReader: CardBadges.Autotrader,
        FeeStatementReader: FeeStatements.ReadAutotrader,
        DetailTitleModelReader: ReadTitleModel);

    /// <summary>What CarMax's own name is stored as when a detail page names no store. A CarMax page
    /// normally names the store the car is at ("CarMax Orlando"), and a page that does not is still a
    /// CarMax car.</summary>
    public const string CarMaxDealerName = "CarMax";

    /// <summary>CarMax's model path segment for a scenario model: the make's own path, then the base
    /// model, then "/hybrid" for a hybrid variant of a base model ("Corolla Hybrid" is
    /// <c>corolla/hybrid</c>, "Camry Hybrid" is <c>camry/hybrid</c>), and just the model for one that is
    /// hybrid by name ("prius", "insight"). The site redirects <c>corolla-hybrid</c> to
    /// <c>corolla/hybrid</c>, so the slash form is built directly and no redirect is followed.</summary>
    private static string CarMaxModelPath(string model) =>
        IsHybridVariant(model) ? $"{Slugify(BaseModelName(model))}/hybrid" : Slugify(model);

    /// <summary>CarMax's search URL for <paramref name="query"/>, which takes the model year range in its
    /// path: from the scenario's minimum year through the year after <paramref name="currentYear"/>, since
    /// a dealer stocks next year's model year before the year turns. The radius is deliberately not read
    /// from the query: <c>distance=nationwide</c> is a per-site override of the scenario's radius. CarMax
    /// transfers a car from any of its stores for a per-car fee printed on the card (as little as $49),
    /// so a car far from the buyer is a real candidate here, and a radius would hide most of the stock
    /// (a nationwide Prius search matched 526 cars where the default radius matched 4).</summary>
    public static string CarMaxSearchUrl(ListingQuery query, int currentYear) =>
        $"https://www.carmax.com/cars/{Slugify(query.Make)}/{CarMaxModelPath(query.Model)}/{query.YearMin}-{currentYear + 1}" +
        $"?zip={query.Zip}&distance=nationwide&mileage={query.MaxMileage}";

    /// <summary>CarMax: its own retail stock, sold at one no-haggle price, with a per-car transfer fee
    /// printed on the search card ("$49 shipping·Get it by Monday", or "Available today·Orlando" for a car
    /// at a nearby store, see <see cref="CarMaxCards"/>). A search page states the filtered count first
    /// ("526 matches") and later prints site-wide and category totals ("86,317 matches"), so the count is
    /// the first line matching <c>N matches</c>, and a "Show 25 matches" control is not one (see
    /// <see cref="WalkSite.MatchCountPattern"/>). Cards load behind that control, which the walk presses
    /// until the count is covered or nothing new appears (see <see cref="SearchPageLoadMore"/>), so the
    /// site is not paged by URL. A detail link is <c>/car/&lt;digits&gt;</c>, a stock number, and the
    /// detail page's visible text does not print the VIN but its HTML does (see <see cref="CarMaxVin"/>).
    /// Its <see cref="WalkSite.CardPriceReader"/> (<see cref="CarMaxCards.ReadPrice"/>) reads the first dollar
    /// amount that isn't its shipping fee or its monthly estimate, so a known CarMax listing is kept current
    /// from its card, price included, exactly as any other site's is.</summary>
    public static readonly WalkSite CarMax = new(
        "carmax",
        query => [CarMaxSearchUrl(query, TimeProvider.System.GetLocalNow().Year)],
        new Regex(@"^https?://(?:www\.)?carmax\.com/car/\d+", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        FallbackDealerName: CarMaxDealerName,
        MatchCountPattern: new Regex(@"(?<!Show\s)(?<![\d,])(\d[\d,]*)\s+match(?:es)?\b", RegexOptions.IgnoreCase),
        CardPriceReader: CarMaxCards.ReadPrice,
        CardFeeReader: CarMaxCards.ReadFee,
        DetailHtmlVinReader: CarMaxVin.Read,
        DetailDealerReader: CarMaxStores.Read,
        DetailAvailabilityReader: CarMaxStores.ReadAvailability,
        LoadMoreControlPattern: new Regex(@"^\s*(?:Show\s+\d+\s+match(?:es)?|Load\s+more)\s*$", RegexOptions.IgnoreCase),
        DetailTitleModelReader: ReadTitleModel,
        // The search URL's own year range and mileage facet are not actually honored by the site (see
        // CarMaxCards.ReadVehicleFacets), so a card that already fails either is dropped here. Its price
        // facet is unverified too (no live page has confirmed it bounds results, the way the year and
        // mileage facets were shown not to), so the search URL below never carries one; the scenario's
        // maxPrice is only ever enforced by the per-card price-ceiling check every site shares (see
        // CollectDetailCards), which needs no site cooperation at all.
        CardFacetsReader: CarMaxCards.ReadVehicleFacets);

    /// <summary>CarGurus: a marketplace of dealers' cars, searched by the ids CarGurus gives the make and model (see
    /// <see cref="CarGurusSearch"/>) within the scenario's radius. A model with a <see cref="ListingQuery.HybridOnlyFromModelYear"/>
    /// rule gets two searches, the same reason cars.com does (see the class remarks above): CarGurus files the
    /// hybrid-only-from-year model's post-cutover years under its plain base-model id rather than the hybrid one
    /// (Toyota's 2025+ Camry sits under the id "Camry" itself resolves to, not "Camry Hybrid"'s), so the walk issues
    /// one search for the hybrid model id from the scenario's minimum year and a second for the base model id from
    /// the hybrid-only year (or the scenario's minimum year if that is later). The two share the pair's cap exactly
    /// as cars.com's do (see <see cref="WalkPairSearches"/>), and cards from both are de-duplicated by canonical
    /// URL the same way; each search stops paging by <see cref="WalkSearchPages"/>'s own rules, its stated count
    /// reached or a page adding nothing new, so a pair with the rule reads complete only once both searches have
    /// covered their own stated counts within tolerance (see <see cref="WalkSearchCoverage"/>).
    /// A search page states "N vehicles found", and that
    /// count is of the ordinary results only: the page also carries sponsored cards, whose links say
    /// <c>sponsoredType=PRIORITY</c> (a dealer's ad), <c>FEATURED</c> or <c>HIGHLIGHT</c> (a promoted copy of a
    /// listing shown again at the top of a page) where an ordinary result's says <c>NONE</c>, and those are never
    /// candidates (see <see cref="WalkSite.SponsoredLinkPattern"/>). The site pages by <c>page=N</c> plus a
    /// <c>pageAlignment</c> its first page hands out (see <see cref="CarGurusSearch.PagedSearchUrl"/>), about twenty
    /// cards a page, so the walk follows it until the count is covered or a page adds nothing. A detail link is
    /// <c>/details/&lt;digits&gt;</c> followed by a query string of the search's own, which
    /// <see cref="CanonicalDetailUrl"/> drops.
    /// The card is where the price is read, and it is not the detail page's: a card shows what the buyer pays
    /// delivered ("Price includes $462 shipping" is inside it), while a store-transfer detail page also lists the dealer's price at its lot
    /// (see <see cref="WalkSite.AskingPriceFromCard"/>). The card also says whether the price includes the dealer's fees
    /// (see <see cref="FeeStatements.ReadCarGurusCard"/>) and carries CarGurus's deal badge (see
    /// <see cref="CardBadges.CarGurus"/>). A card can instead state plainly it has none ("No Price Listed"), even
    /// while it names a shipping amount of its own ("Price includes $1,498 shipping"), and such a card is dropped
    /// before it is ever a candidate (see <see cref="CarGurusCards.NoPriceListed"/>) rather than spending a detail
    /// visit finding that out the slow way. The detail page's text prints the VIN, so no HTML reader is needed for
    /// it, and its dealer name and location are read from its own text (see <see cref="CarGurusDealer"/>) rather
    /// than left to the extraction, since CarGurus has no fallback dealer for a page that names none.</summary>
    public static readonly WalkSite CarGurus = new(
        "cargurus",
        query => query.HybridOnlyFromModelYear is int hybridOnlyYear
            ? [
                CarGurusSearch.SearchUrl(query, query.Model, query.YearMin),
                CarGurusSearch.SearchUrl(query, BaseModelName(query.Model), Math.Max(hybridOnlyYear, query.YearMin)),
              ]
            : [CarGurusSearch.SearchUrl(query)],
        new Regex(@"^https?://(?:www\.)?cargurus\.com/details/\d+", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        MatchCountPattern: new Regex(@"(?<![\d,])(\d[\d,]*)\s+vehicles?\s+found\b", RegexOptions.IgnoreCase),
        SponsoredLinkPattern: new Regex(@"[?&]sponsoredType=(?!NONE(?:&|$))", RegexOptions.IgnoreCase),
        PagedSearchUrl: CarGurusSearch.PagedSearchUrl,
        PagingTokenReader: CarGurusSearch.ReadPagingToken,
        CardPriceReader: CardPrices.CarGurusPrice,
        AskingPriceFromCard: true,
        CardBadgeReader: CardBadges.CarGurus,
        CardFeeReader: CarGurusCards.ReadFee,
        CardFeeStatementReader: FeeStatements.ReadCarGurusCard,
        NoPriceCardPattern: CarGurusCards.NoPriceListed,
        DetailDealerReader: CarGurusDealer.Read,
        DetailTitleModelReader: ReadTitleModel);

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        "autotrader" => Autotrader,
        "carmax" => CarMax,
        "cargurus" => CarGurus,
        _ => null,
    };
}
