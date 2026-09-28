namespace Odonomics.Domain;

/// <summary>
/// The scenario's hard pass/fail rules. <see cref="MinModelYearOverrides"/> is keyed by
/// "Make Model" (e.g. "Toyota Camry Hybrid") and wins over <see cref="MinModelYear"/> when
/// present, since the daughter scenario allows a 2018 Camry Hybrid while every other target
/// model starts at 2019. <see cref="MaxPrice"/> is optional: null means no ceiling, so a
/// scenario written before this field existed still ranks and walks exactly as it did.
/// </summary>
public sealed record HardFilters
{
    public required int MinModelYear { get; init; }
    public required IReadOnlyDictionary<string, int> MinModelYearOverrides { get; init; }
    public required int MaxMileage { get; init; }
    public required IReadOnlyList<string> AllowedModels { get; init; }
    public int? MaxPrice { get; init; }

    public int MinYearFor(string makeModel) =>
        MinModelYearOverrides.GetValueOrDefault(makeModel, MinModelYear);
}
