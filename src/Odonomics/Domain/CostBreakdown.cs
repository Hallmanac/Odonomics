namespace Odonomics.Domain;

/// <summary>Every cost line the scenario produces for one vehicle at one price, each carrying a
/// band when any scenario input feeding it was loose.</summary>
public sealed record CostBreakdown
{
    public required decimal PurchaseCost { get; init; }
    public required Band Payment { get; init; }
    public required Band Fuel { get; init; }
    public required Band Maintenance { get; init; }
    public required Band Reserve { get; init; }
    public required decimal InsuranceMonthly { get; init; }
    public required Band Depreciation { get; init; }
    public required Band ResidualValue { get; init; }
    public required Band DuringLoanMonthly { get; init; }
    public required Band AfterPayoffMonthly { get; init; }
    public required Band TenYearTotal { get; init; }
    public required Band TenYearAverageMonthly { get; init; }
}
