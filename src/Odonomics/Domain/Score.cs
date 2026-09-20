namespace Odonomics.Domain;

/// <summary>The scorer's verdict on one vehicle under one scenario: filter result, insurance
/// availability, and the cost breakdown when both of those allow one to be computed.</summary>
public sealed record Score
{
    public required VehicleForScoring Vehicle { get; init; }
    public required bool Passes { get; init; }
    public required IReadOnlyList<string> FailureReasons { get; init; }
    public required bool InsuranceUnknown { get; init; }
    public CostBreakdown? Cost { get; init; }
}
