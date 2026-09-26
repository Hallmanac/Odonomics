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
/// name or none at all. <paramref name="PagedSearchUrl"/> is null for a site whose search is one page
/// (autotrader), and for one whose results run to more pages (cars.com, carvana) it turns a search URL, a 1-based page number, and the
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
/// a dollar amount (the default, see <see cref="SearchPageLinks"/>) is not the card. <paramref name="CardBadgeReader"/>
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
/// matches the label of the control a site's search page has to be pressed at to show more cards ("Show 25 matches"),
/// for a site that loads its cards that way instead of by page number (see <see cref="SearchPageLoadMore"/>).
/// <paramref name="SponsoredLinkPattern"/> matches a detail link a search page carries for a sponsored card (cargurus's
/// <c>sponsoredType=PRIORITY</c>, <c>FEATURED</c>, and <c>HIGHLIGHT</c>, everything but <c>NONE</c>): such a link is never a candidate, since a sponsored card ignores the search's facets and
/// is not one of the counted results. <paramref name="PagingTokenReader"/> reads, off the HTML of a search's first page, the
/// value the URLs of its later pages have to carry for them to line up with it (cargurus's <c>pageAlignment</c>), and
/// <paramref name="PagedSearchUrl"/> then takes it as its third argument. <paramref name="CardFeeStatementReader"/> reads what a
/// result card says about the fees behind its price (cargurus, see <see cref="FeeStatements.ReadCarGurusCard"/>), for a site whose card says
/// it and whose detail page does not agree with it; when it is set, the card's statement is the one a posting is stored with.
/// <paramref name="AskingPriceFromCard"/> says the card's price is the one to store, for a site whose card shows what the buyer pays
/// delivered (cargurus, whose detail page shows the car's price at its lot, shipping not in it).
/// <paramref name="DetailDealerReader"/> reads the dealer a detail page names, ahead of the extraction, for a site whose
/// dealer is one of many stores under a single name that the extraction does not reliably tell from the site itself
/// (carmax, see <see cref="CarMaxStores"/>); null for a site whose dealer the extraction reads.</summary>
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
    Func<string, ResolvedDealer?>? DetailDealerReader = null)
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
    /// kept, carrying the first card text any of its links has.</summary>
    public IReadOnlyList<PageLink> CollectDetailCards(IReadOnlyList<PageLink> links, int poolSize, string? searchPageText = null)
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

        return
        [
            .. detailLinks
                .GroupBy(l => WalkSites.CanonicalDetailUrl(l.Href))
                .Where(g => !skipped.Contains(g.Key))
                .Select(g => g.First() with { CardText = g.Select(l => l.CardText).FirstOrDefault(t => t.Length > 0) ?? "" })
                .Take(poolSize)
        ];
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

    /// <summary>The dealer name to store for a page whose extraction returned
    /// <paramref name="extractedDealerName"/>: that name (trimmed) when the page gave one, such as
    /// a carvana hub ("Carvana Winder"), otherwise <see cref="FallbackDealerName"/>, which is null
    /// for a site with none.</summary>
    public string? ResolveDealerName(string? extractedDealerName) =>
        string.IsNullOrWhiteSpace(extractedDealerName)
            ? FallbackDealerName
            : extractedDealerName.Trim();

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
    /// extraction returned, and is not a fallback either.</summary>
    public ResolvedDealer ResolveDealer(string? extractedDealerName, string? extractedDealerLocation, string? pageText = null)
    {
        if (ReadsAsPrivateSeller(pageText))
        {
            return new ResolvedDealer(WalkSites.PrivateSellerDealerName, null, IsFallback: false);
        }

        if (pageText is not null && DetailDealerReader?.Invoke(pageText) is { } pageDealer)
        {
            return pageDealer;
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
}

/// <summary>One anchor read off a search page: its resolved href, its visible text, which on a
/// cars.com card link is the card's title ("Used 2024 Toyota Corolla LE"), and the text of the result
/// card the anchor sits in (empty when the pull found none, see <see cref="SearchPageLinks"/>).</summary>
public readonly record struct PageLink(string Href, string Text, string CardText = "");

/// <summary>One loaded search page: its anchors, its visible text for a site that states its
/// match count there (see <see cref="WalkSite.MatchCountPattern"/>), and the paging token its HTML gave, for a site whose
/// later pages have to carry one (see <see cref="WalkSite.PagingTokenReader"/>).</summary>
public readonly record struct SearchPageContent(IReadOnlyList<PageLink> Links, string? Text = null, string? PagingToken = null);

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
        // cars.com pages with a plain page=N on the same URL. A page holds about thirty cards, and the
        // site ignores a page_size parameter, so the search URL carries none.
        PagedSearchUrl: (searchUrl, pageNumber, _) => $"{searchUrl}&page={pageNumber}",
        CardBadgeReader: CardBadges.CarsCom,
        FeeStatementReader: FeeStatements.ReadCarsCom);

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
        CardBadgeReader: CardBadges.Carvana,
        // The delivery block also carries the pickup option, and renders only after the page has been
        // scrolled about a third of the way down.
        PickupReader: CarvanaPickup.Read,
        LazyDetailBlockMarker: CarvanaPickup.BlockHeading);

    /// <summary>What a private seller's listing is stored as: one dealer row for every private seller,
    /// with no location, so no individual's name or city enters the ledger and
    /// <c>odo dealer grade</c> has one row to skip rather than a person to look up on CarEdge (see
    /// <see cref="PrivateSellerDealers"/>).</summary>
    public const string PrivateSellerDealerName = "Private seller";

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
    /// #purchaseConfidence, both of which <see cref="CanonicalDetailUrl"/> strips. It has no
    /// <see cref="WalkSite.CardPriceReader"/> yet: the recorded search page shows a card's price as bare
    /// digits ("26,093", no dollar sign) and no recorded page carries a card's own markup, so its card
    /// shape is not confirmed, and a known autotrader listing is seen again from its card without a
    /// price until it is.</summary>
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
        CardBadgeReader: CardBadges.Autotrader,
        FeeStatementReader: FeeStatements.ReadAutotrader);

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
    /// It has no <see cref="WalkSite.CardPriceReader"/>: no recorded card shows which dollar amount on it is
    /// the price, so a known CarMax listing is seen again from its card without a price until one does.</summary>
    public static readonly WalkSite CarMax = new(
        "carmax",
        query => [CarMaxSearchUrl(query, TimeProvider.System.GetLocalNow().Year)],
        new Regex(@"^https?://(?:www\.)?carmax\.com/car/\d+", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2,
        FallbackDealerName: CarMaxDealerName,
        MatchCountPattern: new Regex(@"(?<!Show\s)(?<![\d,])(\d[\d,]*)\s+match(?:es)?\b", RegexOptions.IgnoreCase),
        CardFeeReader: CarMaxCards.ReadFee,
        DetailHtmlVinReader: CarMaxVin.Read,
        DetailDealerReader: CarMaxStores.Read,
        LoadMoreControlPattern: new Regex(@"^\s*Show\s+\d+\s+match(?:es)?\s*$", RegexOptions.IgnoreCase));

    /// <summary>CarGurus: a marketplace of dealers' cars, searched by the ids CarGurus gives the make and model (see
    /// <see cref="CarGurusSearch"/>) within the scenario's radius. A search page states "N vehicles found", and that
    /// count is of the ordinary results only: the page also carries sponsored cards, whose links say
    /// <c>sponsoredType=PRIORITY</c> (a dealer's ad), <c>FEATURED</c> or <c>HIGHLIGHT</c> (a promoted copy of a
    /// listing shown again at the top of a page) where an ordinary result's says <c>NONE</c>, and those are never
    /// candidates (see <see cref="WalkSite.SponsoredLinkPattern"/>). The site pages by <c>page=N</c> plus a
    /// <c>pageAlignment</c> its first page hands out (see <see cref="CarGurusSearch.PagedSearchUrl"/>), about twenty
    /// cards a page, so the walk follows it until the count is covered or a page adds nothing. A detail link is
    /// <c>/details/&lt;digits&gt;</c> followed by a query string of the search's own, which
    /// <see cref="CanonicalDetailUrl"/> drops.
    /// The card is where the price is read, and it is not the detail page's: a card shows what the buyer pays
    /// delivered ("Price includes $462 shipping" is inside it), while the detail page shows the car's price at its lot
    /// (see <see cref="WalkSite.AskingPriceFromCard"/>). The card also says whether the price includes the dealer's fees
    /// (see <see cref="FeeStatements.ReadCarGurusCard"/>) and carries CarGurus's deal badge (see
    /// <see cref="CardBadges.CarGurus"/>). The detail page's text prints the VIN, so no HTML reader is needed for it.</summary>
    public static readonly WalkSite CarGurus = new(
        "cargurus",
        query => [CarGurusSearch.SearchUrl(query)],
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
        CardFeeStatementReader: FeeStatements.ReadCarGurusCard);

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
