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
/// maximum mileage, which both sites take as search facets so the per-pair cap is spent only on
/// cars the scenario can rank. <paramref name="FallbackDealerName"/>
/// is the dealer a posting is stamped with when its detail page names none, for a site where the
/// site itself is the seller (carvana); null for a marketplace whose pages carry the dealer's own
/// name or none at all.</summary>
public sealed record WalkSite(
    string Name,
    Func<ListingQuery, IReadOnlyList<string>> BuildSearchUrls,
    Regex DetailUrlPattern,
    int DetailLinkOverfetchMultiplier = 1,
    string? FallbackDealerName = null,
    Regex? SkippedCardTitlePattern = null)
{
    /// <summary>The candidate detail links on a search page, in page order, at most
    /// <paramref name="poolSize"/> of them: every link this site's <see cref="DetailUrlPattern"/>
    /// matches, one per canonical URL, minus any listing whose card title matches
    /// <see cref="SkippedCardTitlePattern"/> (cars.com mixes new-car cards into a used search, and
    /// the scenario can never rank one). A listing is skipped when any of its anchors carries such
    /// a title, since a card links its photo and its title separately and only the title anchor
    /// has text. A skipped card never enters the pool, so it never counts against the per-pair cap.</summary>
    public IReadOnlyList<string> CollectDetailLinks(IReadOnlyList<PageLink> links, int poolSize)
    {
        List<PageLink> detailLinks = [.. links.Where(l => DetailUrlPattern.IsMatch(l.Href))];
        HashSet<string> skipped = SkippedCardTitlePattern is null
            ? []
            : [.. detailLinks
                .Where(l => SkippedCardTitlePattern.IsMatch(l.Text))
                .Select(l => WalkSites.CanonicalDetailUrl(l.Href))];

        return
        [
            .. detailLinks
                .DistinctBy(l => WalkSites.CanonicalDetailUrl(l.Href))
                .Where(l => !skipped.Contains(WalkSites.CanonicalDetailUrl(l.Href)))
                .Select(l => l.Href)
                .Take(poolSize)
        ];
    }

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
    /// replaces a link to a more specific dealer an earlier sighting established.</summary>
    public ResolvedDealer ResolveDealer(string? extractedDealerName, string? extractedDealerLocation) =>
        FallbackDealerName is not null && NamesNoDealerBeyondTheSite(extractedDealerName)
            ? new ResolvedDealer(FallbackDealerName, null, IsFallback: true)
            : new ResolvedDealer(ResolveDealerName(extractedDealerName), extractedDealerLocation, IsFallback: false);

    private bool NamesNoDealerBeyondTheSite(string? extractedDealerName) =>
        string.IsNullOrWhiteSpace(extractedDealerName)
        || DealerNormalizer.Normalize(extractedDealerName) == DealerNormalizer.Normalize(FallbackDealerName);
}

/// <summary>One anchor read off a search page: its resolved href and its visible text, which on a
/// cars.com card link is the card's title ("Used 2024 Toyota Corolla LE").</summary>
public readonly record struct PageLink(string Href, string Text);

/// <summary>The dealer name and location a walked candidate is stored with, and whether the name is
/// the site's fallback rather than one the page gave.</summary>
public readonly record struct ResolvedDealer(string? Name, string? Location, bool IsFallback);

/// <summary>Search-URL shapes and detail-link patterns for the two v0 walk targets. The spike's
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
    /// parameter. Both walk targets append a per-search-session id (cars.com's "sid", carried on
    /// every "/vehicledetail/" href on the page) and cars.com additionally emits more than one
    /// query-string variant of the same card's link ("?sid=…" and
    /// "?openLeadForm=true&amp;sid=…"). Canonicalizing before the walk dedupes those variants
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
        FallbackDealerName: CarvanaDealerName);

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        _ => null,
    };
}
