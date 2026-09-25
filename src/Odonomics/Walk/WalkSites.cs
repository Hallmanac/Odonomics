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
/// link to replace it with rather than shortening the pair; see README.md's walk section for the
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
/// and for one whose results run to more pages it turns a search URL and a 1-based page number into
/// that page's URL (see <see cref="WalkSearchPages"/>). <paramref name="MatchCountPattern"/> is for a site whose search page states
/// how many listings match ("13 Matches") and keeps filling the page with cards for other models and
/// years after them (autotrader): its first group is that count, and
/// <see cref="CollectDetailLinks"/> trusts the count over the cards. <paramref name="ResultCardLinkPattern"/>
/// says which of a counted page's links are its result cards (autotrader's <c>clickType=listing</c>), since
/// the count does not include the sponsored card that sits first in page order. <paramref name="PrivateSellerPagePattern"/>
/// is for a site whose detail page marks a private seller in its own text (autotrader's
/// "Sample S (Private Seller)" line): a page it matches is stored with
/// <see cref="WalkSites.PrivateSellerDealerName"/> and no location, whatever name the extraction
/// read off it. <paramref name="ShippingFeeReader"/> reads the one-time shipping fee off a detail page's
/// text for a site that prints one (carvana); null for a site that does not, whose postings store no
/// fee.</summary>
public sealed record WalkSite(
    string Name,
    Func<ListingQuery, IReadOnlyList<string>> BuildSearchUrls,
    Regex DetailUrlPattern,
    int DetailLinkOverfetchMultiplier = 1,
    string? FallbackDealerName = null,
    Regex? SkippedCardTitlePattern = null,
    Func<string, int, string>? PagedSearchUrl = null,
    Regex? MatchCountPattern = null,
    Regex? PrivateSellerPagePattern = null,
    Regex? ResultCardLinkPattern = null,
    Func<string, decimal?>? ShippingFeeReader = null)
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
    public IReadOnlyList<string> CollectDetailLinks(IReadOnlyList<PageLink> links, int poolSize, string? searchPageText = null)
    {
        int? statedCount = MatchCountIn(searchPageText);
        if (statedCount is int matchCount)
        {
            poolSize = Math.Min(poolSize, matchCount);
        }

        List<PageLink> detailLinks = [.. links.Where(l => DetailUrlPattern.IsMatch(l.Href))];
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
                .DistinctBy(l => WalkSites.CanonicalDetailUrl(l.Href))
                .Where(l => !skipped.Contains(WalkSites.CanonicalDetailUrl(l.Href)))
                .Select(l => l.Href)
                .Take(poolSize)
        ];
    }

    private int? MatchCountIn(string? searchPageText)
    {
        Match match = MatchCountPattern is null || searchPageText is null
            ? Match.Empty
            : MatchCountPattern.Match(searchPageText);
        return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", ""), out int count)
            ? count
            : null;
    }

    /// <summary>The shipping fee a detail page shows on top of its asking price, or null when this
    /// site prints none or the page carries none.</summary>
    public decimal? ReadShippingFee(string pageText) => ShippingFeeReader?.Invoke(pageText);

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
    /// not a dealer row. That name is a fact the page states, not a fallback, so it is not flagged as one.</summary>
    public ResolvedDealer ResolveDealer(string? extractedDealerName, string? extractedDealerLocation, string? pageText = null) =>
        ReadsAsPrivateSeller(pageText)
            ? new ResolvedDealer(WalkSites.PrivateSellerDealerName, null, IsFallback: false)
            : FallbackDealerName is not null && NamesNoDealerBeyondTheSite(extractedDealerName)
                ? new ResolvedDealer(FallbackDealerName, null, IsFallback: true)
                : new ResolvedDealer(ResolveDealerName(extractedDealerName), extractedDealerLocation, IsFallback: false);

    private bool ReadsAsPrivateSeller(string? pageText) =>
        PrivateSellerPagePattern is not null && pageText is not null && PrivateSellerPagePattern.IsMatch(pageText);

    private bool NamesNoDealerBeyondTheSite(string? extractedDealerName) =>
        string.IsNullOrWhiteSpace(extractedDealerName)
        || DealerNormalizer.Normalize(extractedDealerName) == DealerNormalizer.Normalize(FallbackDealerName);
}

/// <summary>One anchor read off a search page: its resolved href and its visible text, which on a
/// cars.com card link is the card's title ("Used 2024 Toyota Corolla LE").</summary>
public readonly record struct PageLink(string Href, string Text);

/// <summary>One loaded search page: its anchors, and its visible text for a site that states its
/// match count there (see <see cref="WalkSite.MatchCountPattern"/>).</summary>
public readonly record struct SearchPageContent(IReadOnlyList<PageLink> Links, string? Text = null);

/// <summary>The dealer name and location a walked candidate is stored with, and whether the name is
/// the site's fallback rather than one the page gave.</summary>
public readonly record struct ResolvedDealer(string? Name, string? Location, bool IsFallback);

/// <summary>Search-URL shapes and detail-link patterns for the walk targets: cars.com and carvana, whose
/// hybrid facets the rest of this comment is about, and autotrader (see <see cref="Autotrader"/>). The spike's
/// SPIKE-FINDINGS.md recorded both sites as having no working hybrid facet, but that recording
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
        SkippedCardTitlePattern: new Regex(@"^\s*New\s", RegexOptions.IgnoreCase));

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
        PagedSearchUrl: (searchUrl, pageNumber) => $"{searchUrl}&page={pageNumber}",
        ShippingFeeReader: CarvanaShipping.Read);

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
    /// #purchaseConfidence, both of which <see cref="CanonicalDetailUrl"/> strips.</summary>
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
        ResultCardLinkPattern: new Regex(@"[?&]clickType=listing(?:&|$)"));

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        "autotrader" => Autotrader,
        _ => null,
    };
}
