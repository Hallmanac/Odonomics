using Odonomics.Domain;

namespace Odonomics.Sources;

/// <summary>One target model's search parameters, derived from the scenario rather than
/// hardcoded, unlike the spike's fixed QueryGroup list.</summary>
public sealed record ListingQuery(string Make, string Model, int YearMin, string Zip, int RadiusMiles, int MaxMileage)
{
    public bool MatchesYear(int? year) => year is not null && year >= YearMin;

    public bool MatchesMileage(int? mileage) => mileage is null || mileage <= MaxMileage;

    private bool IsHybridVariant => Model.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    private string BaseModelName => IsHybridVariant
        ? Model[..Model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase)]
        : Model;

    /// <summary>Whether a candidate a source (an API response or a walked detail page) actually is
    /// the model this query asked for, rather than generic make inventory the source fell back to
    /// for an unrecognized or compound model facet. The spike found exactly this on cars.com and
    /// Carvana: a "Camry Hybrid" or "Corolla Hybrid" walk came back with gas trims mixed in, since
    /// the search URL always asks for the base model (see WalkSites.BaseModelSlug) and the site
    /// lists both together. A candidate is never trusted as this query's own model just because it
    /// came back for this query; it has to actually say so.</summary>
    public bool MatchesExtractedVehicle(string? make, string? model, string? trim)
    {
        if (make is not null && !make.Contains(Make, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string modelAndTrim = $"{model} {trim}";
        if (!modelAndTrim.Contains(BaseModelName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsHybridVariant && !modelAndTrim.Contains("Hybrid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>Builds one query per target model in the scenario's allowed-models list. Each
    /// entry is a "Make Model" string (e.g. "Toyota Camry Hybrid"); the make is always the first
    /// word, matching how every target model in this scenario is named.</summary>
    public static IReadOnlyList<ListingQuery> FromScenario(Scenario scenario) =>
        [.. scenario.Filters.AllowedModels.Select(makeModel =>
        {
            int spaceIndex = makeModel.IndexOf(' ');
            string make = spaceIndex < 0 ? makeModel : makeModel[..spaceIndex];
            string model = spaceIndex < 0 ? "" : makeModel[(spaceIndex + 1)..];
            return new ListingQuery(make, model, scenario.Filters.MinYearFor(makeModel), scenario.Zip, scenario.RadiusMiles, scenario.Filters.MaxMileage);
        })];
}
