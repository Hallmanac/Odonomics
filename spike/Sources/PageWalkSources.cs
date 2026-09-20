using System.Text.RegularExpressions;
using Spike.Models;

namespace Spike.Sources;

public static class PageWalkSources
{
    private static string Slugify(string value) => value.ToLowerInvariant().Replace(" ", "-");

    /// <summary>
    /// Day one found that guessing a hybrid-specific model slug with a hyphen (e.g.
    /// "corolla-hybrid") makes these sites silently drop the model filter and show generic
    /// inventory for the make instead of erroring, so most callers here only ever ask for the
    /// base model and let QueryGroup.MatchesExtractedVehicle enforce the hybrid distinction
    /// afterward. Cars.com turned out to be the exception: its own model-facet JSON (visible in
    /// a search response body) lists real hybrid-trim slugs, just with an underscore rather than
    /// a hyphen ("toyota-corolla_hybrid"), which CarsComHybridModelSlug uses directly.
    /// </summary>
    private static string BaseModelSlug(string model)
    {
        var hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return Slugify(hybridIndex >= 0 ? model[..hybridIndex] : model);
    }

    private static string CarsComModelSlug(string make, string model)
    {
        var makeSlug = Slugify(make);
        var hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return hybridIndex >= 0
            ? $"{makeSlug}-{Slugify(model[..hybridIndex])}_hybrid"
            : $"{makeSlug}-{Slugify(model)}";
    }

    public static PageWalkListingSource CreateCarsCom(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "cars.com",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                var makeSlug = Slugify(group.Make);
                var modelSlug = CarsComModelSlug(group.Make, group.Model);
                return $"https://www.cars.com/shopping/results/?makes[]={makeSlug}&models[]={modelSlug}" +
                       $"&maximum_distance={group.RadiusMiles}&zip={group.Zip}&stock_type=used";
            },
            detailUrlPattern: new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction);

    public static PageWalkListingSource CreateAutotrader(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "autotrader",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                var makeSlug = Slugify(group.Make);
                var modelSlug = BaseModelSlug(group.Model);
                return $"https://www.autotrader.com/cars-for-sale/all-cars/{makeSlug}/{modelSlug}/daytona-beach-fl-{group.Zip}" +
                       $"?searchRadius={group.RadiusMiles}&maxMileage={group.MaxMileage}";
            },
            detailUrlPattern: new Regex("/cars-for-sale/vehicledetails", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction);

    public static PageWalkListingSource CreateCarvana(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "carvana",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                var modelSlug = $"{Slugify(group.Make)}-{BaseModelSlug(group.Model)}";
                return $"https://www.carvana.com/cars/{modelSlug}?zip={group.Zip}";
            },
            detailUrlPattern: new Regex("/vehicle/", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction);

    /// <summary>Runs Cars.com; if it is blocked in every group, falls back to Autotrader per the brief.</summary>
    public static async Task<SourceRunResult> RunAggregatorAsync(
        string profileRoot, RecordedResponses recorded, ExtractionClient extraction, CancellationToken cancellationToken)
    {
        var carsCom = CreateCarsCom(Path.Combine(profileRoot, "cars.com"), recorded, extraction);
        var primary = await carsCom.RunAsync(cancellationToken);
        if (primary.Candidates.Count > 0 || !primary.WasBlocked)
        {
            return primary;
        }

        var autotrader = CreateAutotrader(Path.Combine(profileRoot, "autotrader"), recorded, extraction);
        var fallback = await autotrader.RunAsync(cancellationToken);
        fallback.Failures.Insert(0,
            $"cars.com found {primary.CandidatesFound} candidates on search pages but bot defenses blocked every VIN detail-page visit ({string.Join("; ", primary.Failures)}); fell back to autotrader for VINs");
        return fallback;
    }
}
