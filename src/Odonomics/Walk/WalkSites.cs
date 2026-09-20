using System.Text.RegularExpressions;

namespace Odonomics.Walk;

public sealed record WalkSite(string Name, Func<string, string, string, int, string> BuildSearchUrl, Regex DetailUrlPattern);

/// <summary>Search-URL shapes and detail-link patterns for the two v0 walk targets, ported from
/// spike/Sources/PageWalkSources.cs. Cars.com and Carvana were found on the spike to silently
/// drop an unrecognized hybrid-specific model slug and show generic make inventory instead, so
/// every query asks for the base model only.</summary>
public static class WalkSites
{
    public static string Slugify(string value) => value.ToLowerInvariant().Replace(" ", "-");

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
        new Regex("/vehicledetail/", RegexOptions.IgnoreCase));

    public static readonly WalkSite Carvana = new(
        "carvana",
        (make, model, zip, _) =>
        {
            string modelSlug = $"{Slugify(make)}-{BaseModelSlug(model)}";
            return $"https://www.carvana.com/cars/{modelSlug}?zip={zip}";
        },
        new Regex("/vehicle/", RegexOptions.IgnoreCase));

    public static WalkSite? Find(string name) => name.ToLowerInvariant() switch
    {
        "cars.com" => CarsCom,
        "carvana" => Carvana,
        _ => null,
    };
}
