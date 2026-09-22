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

/// <summary>Search-URL shapes and detail-link patterns for the two v0 walk targets, ported from
/// spike/Sources/PageWalkSources.cs. Neither site has a confirmed model facet that separates a
/// hybrid or plug-in variant from its base model: cars.com's underscored
/// "toyota-corolla_hybrid" looked like a fix because that exact value appears in the site's own
/// model-facet JSON, but the site's own recorded response to that query still resolved back to
/// plain "toyota-corolla" (SPIKE-FINDINGS.md's "sites silently ignore a model facet they don't
/// recognize" surprise has the detail). So every query here asks for the base model only and
/// leans on <see cref="WalkSite.DetailLinkOverfetchMultiplier"/> plus
/// <see cref="Odonomics.Sources.ListingQuery.MatchesExtractedVehicle"/> to fill the per-pair cap
/// with real candidates instead.</summary>
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

    private static string BaseModelSlug(string model)
    {
        int hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return Slugify(hybridIndex >= 0 ? model[..hybridIndex] : model);
    }

    public static readonly WalkSite CarsCom = new(
        "cars.com",
        (make, model, zip, radius) =>
        {
            string makeSlug = Slugify(make);
            string modelSlug = $"{makeSlug}-{BaseModelSlug(model)}";
            return $"https://www.cars.com/shopping/results/?makes[]={makeSlug}&models[]={modelSlug}" +
                   $"&maximum_distance={radius}&zip={zip}&stock_type=used";
        },
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 3);

    public static readonly WalkSite Carvana = new(
        "carvana",
        (make, model, zip, _) =>
        {
            string modelSlug = $"{Slugify(make)}-{BaseModelSlug(model)}";
            return $"https://www.carvana.com/cars/{modelSlug}?zip={zip}";
        },
        new Regex("/vehicle/", RegexOptions.IgnoreCase),
        DetailLinkOverfetchMultiplier: 3);

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        _ => null,
    };
}
