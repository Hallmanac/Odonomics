namespace Odonomics.Domain;

/// <summary>The scorer's verdict on one vehicle under one scenario: filter result, insurance
/// availability, and the cost breakdown when both of those allow one to be computed.</summary>
public sealed record Score
{
    public required VehicleForScoring Vehicle { get; init; }
    public required bool Passes { get; init; }
    public required IReadOnlyList<string> FailureReasons { get; init; }
    public required bool InsuranceUnknown { get; init; }
    public required bool MpgUnknown { get; init; }
    public CostBreakdown? Cost { get; init; }

    /// <summary>The required features (see <see cref="HardFilters.RequiredFeatures"/>) whose status is still
    /// unknown for this vehicle, so `odo rank` can say "confirm ...". Display only: it never excludes the car.</summary>
    public IReadOnlyList<string> UnconfirmedFeatures { get; init; } = [];

    /// <summary>The preferred features (see <see cref="HardFilters.PreferredFeatures"/>) this vehicle is known
    /// to have, so `odo rank` can print their markers. Display only: absent and unknown print nothing, and no
    /// vehicle is excluded or scored differently for it.</summary>
    public IReadOnlyList<string> PreferredPresent { get; init; } = [];
}
