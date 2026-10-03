namespace Odonomics.Domain;

/// <summary>
/// The scenario's hard pass/fail rules. <see cref="MinModelYearOverrides"/> is keyed by
/// "Make Model" (e.g. "Toyota Camry Hybrid") and wins over <see cref="MinModelYear"/> when
/// present, since the daughter scenario allows a 2018 Camry Hybrid while every other target
/// model starts at 2019. <see cref="MaxPrice"/> is optional: null means no ceiling, so a
/// scenario written before this field existed still ranks and walks exactly as it did.
/// <see cref="RequiredFeatures"/> is optional the same way: empty means no equipment is required.
/// </summary>
public sealed record HardFilters
{
    public required int MinModelYear { get; init; }
    public required IReadOnlyDictionary<string, int> MinModelYearOverrides { get; init; }
    public required int MaxMileage { get; init; }
    public required IReadOnlyList<string> AllowedModels { get; init; }
    public int? MaxPrice { get; init; }

    /// <summary>Equipment every candidate must have, by the names in <see cref="EquipmentFeatures"/>
    /// ("smart-key entry", "keyless entry", "push-button start"). A car whose status for one is confirmed absent is excluded
    /// with the feature named; one whose status is unknown stays and gets a "confirm ..." note instead.
    /// <see cref="ScenarioLoader"/> rejects a name that is not a tracked feature.</summary>
    public IReadOnlyList<string> RequiredFeatures { get; init; } = [];

    public int MinYearFor(string makeModel) =>
        MinModelYearOverrides.GetValueOrDefault(makeModel, MinModelYear);
}
