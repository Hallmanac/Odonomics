using System.Text.Json;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>One walk target's search-URL builder, detail-link pattern, and how much the walk
/// should over-fetch candidate links to make up for a search URL that cannot filter down to the
/// exact requested model. <see cref="DetailLinkOverfetchMultiplier"/> is 1 (no over-fetch) for a
/// site whose search URL already isolates the requested model, and greater than 1 for a site
/// whose search results can mix in other models the walk then has to reject.</summary>
public sealed record WalkSite(
    string Name,
    Func<string, string, string, int, string> BuildSearchUrl,
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
/// operator-built URL is what the walk now trusts. cars.com's model facet joins every word of the
/// model name with underscores after the make and a hyphen ("Toyota Corolla Hybrid" ->
/// models[]=toyota-corolla_hybrid), and carvana has no separate hybrid model at all; it filters
/// its base-model search by fuel type (parentModels: "Corolla" plus fuelTypes: ["Hybrid"])
/// instead.</summary>
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

    /// <summary>cars.com's model-facet value for a model name: every word lowercased and joined
    /// with underscores ("Corolla Hybrid" -> "corolla_hybrid", "Corolla Cross" ->
    /// "corolla_cross"), matching the exact value the site's own browser puts in models[] when
    /// that model is ticked in the left-column facet.</summary>
    private static string ModelFacetWords(string model) => model.ToLowerInvariant().Replace(" ", "_");

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
        (make, model, zip, radius) =>
        {
            string makeSlug = Slugify(make);
            string modelSlug = $"{makeSlug}-{ModelFacetWords(model)}";
            return $"https://www.cars.com/shopping/results/?stock_type=used&makes[]={makeSlug}" +
                   $"&models[]={modelSlug}&zip={zip}&maximum_distance={radius}";
        },
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 2);

    public static readonly WalkSite Carvana = new(
        "carvana",
        (make, model, zip, _) =>
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
