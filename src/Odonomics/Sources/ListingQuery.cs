using Odonomics.Domain;

namespace Odonomics.Sources;

/// <summary>One target model's search parameters, derived from the scenario rather than
/// hardcoded, unlike the spike's fixed QueryGroup list. <paramref name="HybridOnlyFromModelYear"/>
/// is the scenario's own rule (Scenario.HybridOnlyFromModelYear) for this query's model, when it
/// has one: the model year this base model went hybrid-only, letting a candidate below that year
/// still fail the hybrid check while one at or above it passes without the listing ever saying
/// "Hybrid".</summary>
public sealed record ListingQuery(string Make, string Model, int YearMin, string Zip, int RadiusMiles, int MaxMileage, int? HybridOnlyFromModelYear = null)
{
    public bool MatchesYear(int? year) => year is not null && year >= YearMin;

    public bool MatchesMileage(int? mileage) => mileage is null || mileage <= MaxMileage;

    private bool IsHybridVariant => Model.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    private string BaseModelName => IsHybridVariant
        ? Model[..Model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase)]
        : Model;

    private bool MatchesMakeAndBaseModel(string? make, string? model, string? trim)
    {
        if (make is not null && !make.Contains(Make, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return $"{model} {trim}".Contains(BaseModelName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHybridText(string? model, string? trim) =>
        $"{model} {trim}".Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a candidate a source (an API response or a walked detail page) actually is
    /// the model this query asked for, rather than generic make inventory the source fell back to
    /// for an unrecognized or compound model facet. The spike found exactly this on cars.com and
    /// Carvana: a "Camry Hybrid" or "Corolla Hybrid" walk came back with gas trims mixed in. The
    /// walk's own search URLs now use each site's real hybrid facet (see WalkSites), but this
    /// check stays in place as a safety net for whatever still slips past that: an API source with
    /// no facet of its own, a compound model facet, or a degraded page. A candidate is never
    /// trusted as this query's own model just because it came back for this query; it has to
    /// actually say so, unless <see cref="HybridOnlyFromModelYear"/> says this base model has no
    /// gas version left to confuse it with at this candidate's year.</summary>
    public bool MatchesExtractedVehicle(string? make, string? model, string? trim, int? year)
    {
        if (!MatchesMakeAndBaseModel(make, model, trim))
        {
            return false;
        }

        if (!IsHybridVariant || ContainsHybridText(model, trim))
        {
            return true;
        }

        return HybridOnlyFromModelYear is int hybridYear && year is int candidateYear && candidateYear >= hybridYear;
    }

    /// <summary>The year this query's hybrid-only rule takes effect, when a candidate otherwise
    /// matches this query's make and base model but falls below it: lets the walk explain a
    /// rejection as a known gas-only-before-year gap (e.g. "2024 Camry SE, gas-only before 2025")
    /// rather than a generic mismatch. Null when this isn't that case, whatever the reason.</summary>
    public int? GasOnlyBeforeHybridYear(string? make, string? model, string? trim, int? year)
    {
        if (!IsHybridVariant || HybridOnlyFromModelYear is not int hybridYear)
        {
            return null;
        }

        if (!MatchesMakeAndBaseModel(make, model, trim) || ContainsHybridText(model, trim))
        {
            return null;
        }

        return year is int candidateYear && candidateYear < hybridYear ? hybridYear : null;
    }

    /// <summary>Builds one query per target model in the scenario's allowed-models list.</summary>
    public static IReadOnlyList<ListingQuery> FromScenario(Scenario scenario) =>
        [.. scenario.Filters.AllowedModels.Select(makeModel => For(scenario, makeModel))];

    /// <summary>Builds the query for one "Make Model" string (e.g. "Toyota Camry Hybrid"); the make
    /// is always the first word, matching how every target model in this scenario is named. The
    /// minimum model year, maximum mileage, and hybrid-only year all come from the scenario, so a
    /// caller that turns the query into a site's search facets applies the same limits the scorer
    /// later enforces.</summary>
    public static ListingQuery For(Scenario scenario, string makeModel)
    {
        int spaceIndex = makeModel.IndexOf(' ');
        string make = spaceIndex < 0 ? makeModel : makeModel[..spaceIndex];
        string model = spaceIndex < 0 ? "" : makeModel[(spaceIndex + 1)..];
        int? hybridOnlyFromModelYear = scenario.HybridOnlyFromModelYear.TryGetValue(makeModel, out int hybridYear) ? hybridYear : null;
        return new ListingQuery(make, model, scenario.Filters.MinYearFor(makeModel), scenario.Zip, scenario.RadiusMiles, scenario.Filters.MaxMileage, hybridOnlyFromModelYear);
    }
}
