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
/// spike/Sources/PageWalkSources.cs.</summary>
public static class WalkSites
{
    public static string Slugify(string value) => value.ToLowerInvariant().Replace(" ", "-");

    /// <summary>Cars.com's own model facet value: the make and model joined by a hyphen, with
    /// every space inside the model itself turned into an underscore rather than a hyphen. This
    /// is how cars.com separates a hybrid variant from its gas counterpart in the models[] filter
    /// (confirmed against a recorded cars.com search page's own model-facet list: "Corolla
    /// Hybrid" carries the value "toyota-corolla_hybrid", distinct from plain "Corolla"'s
    /// "toyota-corolla") and how it separates "Prius Prime" from "Prius" the same way. Stripping
    /// the qualifier instead, as this used to do, ticks only the base model's box and cars.com
    /// shows that model's inventory with every variant mixed in.</summary>
    private static string CarsComModelSlug(string make, string model) =>
        $"{Slugify(make)}-{model.ToLowerInvariant().Replace(" ", "_")}";

    public static readonly WalkSite CarsCom = new(
        "cars.com",
        (make, model, zip, radius) =>
        {
            string makeSlug = Slugify(make);
            string modelSlug = CarsComModelSlug(make, model);
            return $"https://www.cars.com/shopping/results/?makes[]={makeSlug}&models[]={modelSlug}" +
                   $"&maximum_distance={radius}&zip={zip}&stock_type=used";
        },
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase));

    /// <summary>Carvana's search URL has no confirmed model facet that separates a hybrid variant
    /// from its gas counterpart (its own bot defenses block probing for one), so the walk still
    /// asks for the base model only and relies on <see cref="DetailLinkOverfetchMultiplier"/> plus
    /// the existing model-match rejection to fill the per-pair cap with real candidates.</summary>
    public static readonly WalkSite Carvana = new(
        "carvana",
        (make, model, zip, _) =>
        {
            int hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
            string baseModel = hybridIndex >= 0 ? model[..hybridIndex] : model;
            string modelSlug = $"{Slugify(make)}-{Slugify(baseModel)}";
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
