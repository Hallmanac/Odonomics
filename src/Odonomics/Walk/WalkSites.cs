using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>One walk target's search-URL builder, detail-link pattern, and how much the walk
/// should over-fetch candidate links. <see cref="DetailLinkOverfetchMultiplier"/> stays above 1
/// even for a site whose search URL isolates the requested model, since a page rejected as
/// <see cref="Odonomics.Walk.DetailPageOutcome.Repeat"/> or (rarely, for a hybrid-only-from-year
/// model, see <see cref="BuildSearchUrl"/>'s hybridOnlyFromModelYear parameter)
/// <see cref="Odonomics.Walk.DetailPageOutcome.NotMatching"/> needs a spare link to replace it
/// with rather than shortening the pair; see README.md's walk section for the full reasoning.
/// <paramref name="BuildSearchUrl"/> takes make, model, zip, radius, and whether the scenario
/// marks this model's base model as hybrid-only from some year onward (see
/// <see cref="Odonomics.Domain.Scenario.HybridOnlyFromModelYear"/>).</summary>
public sealed record WalkSite(
    string Name,
    Func<string, string, string, int, bool, string> BuildSearchUrl,
    Regex DetailUrlPattern,
    int DetailLinkOverfetchMultiplier = 1);

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
/// bucket (Toyota's 2025+ Camry is filed under plain "camry", not "camry_hybrid"; the repo's own
/// recorded walk output confirms it). For a model with that rule set, cars.com's search URL below
/// queries the base model instead of the hybrid facet, so those listings are in the pool at all;
/// ListingQuery.MatchesExtractedVehicle is what then accepts the ones at or after the hybrid-only
/// year and rejects the genuinely-gas ones below it. Carvana needs no equivalent fallback: its
/// fuelTypes filter matches each listing's actual fuel type, not its title text, so a
/// hybrid-only-from-year model's newer listings already come back correctly under the base-model
/// query it always uses.</summary>
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
        (make, model, zip, radius, hybridOnlyFromModelYear) =>
        {
            string makeSlug = Slugify(make);
            string facetModel = hybridOnlyFromModelYear ? BaseModelName(model) : model;
            string modelSlug = $"{makeSlug}-{ModelFacetWords(facetModel)}";
            return $"https://www.cars.com/shopping/results/?stock_type=used&makes[]={makeSlug}" +
                   $"&models[]={modelSlug}&zip={zip}&maximum_distance={radius}";
        },
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2);

    public static readonly WalkSite Carvana = new(
        "carvana",
        (make, model, zip, _, _) =>
        {
            string baseModel = BaseModelName(model);
            object filters = IsHybridVariant(model)
                ? new
                {
                    filters = new
                    {
                        makes = new[] { new { name = make, parentModels = new[] { new { name = baseModel } } } },
                        fuelTypes = new[] { "Hybrid" },
                    },
                }
                : new
                {
                    filters = new
                    {
                        makes = new[] { new { name = make, parentModels = new[] { new { name = baseModel } } } },
                    },
                };
            return $"https://www.carvana.com/cars/filters?zip={zip}&cvnaid={EncodeCvnaid(JsonSerializer.Serialize(filters))}";
        },
        new Regex("/vehicle/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2);

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        _ => null,
    };
}
