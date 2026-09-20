using Odonomics.Domain;

namespace Odonomics.Sources;

/// <summary>One target model's search parameters, derived from the scenario rather than
/// hardcoded, unlike the spike's fixed QueryGroup list.</summary>
public sealed record ListingQuery(string Make, string Model, int YearMin, string Zip, int RadiusMiles, int MaxMileage)
{
    public bool MatchesYear(int? year) => year is not null && year >= YearMin;

    public bool MatchesMileage(int? mileage) => mileage is null || mileage <= MaxMileage;

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
