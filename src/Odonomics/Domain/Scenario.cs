using System.Text.Json.Serialization;

namespace Odonomics.Domain;

/// <summary>
/// Every assumption that drives the cost model for one purchase. See the money definitions in
/// the v0 brief for how each field is used; scenarios/daughter.json is the shipped instance.
/// </summary>
public sealed record Scenario
{
    public required string Name { get; init; }
    public required string Zip { get; init; }
    public required int RadiusMiles { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter AnnualMiles { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter GasPricePerGallon { get; init; }

    public required int HoldYears { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter DownPayment { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter Apr { get; init; }

    public required int TermMonths { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter MaintenancePerMile { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter EmergencyReservePerMonth { get; init; }

    public required decimal SalesTaxStateRate { get; init; }
    public required decimal CountySurtaxRate { get; init; }
    public required string CountySurtaxSource { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter Fees { get; init; }

    [JsonConverter(typeof(ParameterJsonConverter))]
    public required Parameter ResidualFraction { get; init; }

    /// <summary>Keyed by "Make Model"; a missing key or a null value both mean unknown, never zero.</summary>
    public required IReadOnlyDictionary<string, decimal?> InsuranceMonthlyByModel { get; init; }

    /// <summary>Keyed by "Make Model"; EPA combined mpg. The VIN decode would override this when
    /// it knows better, but vPIC does not return fuel economy, so v0 always uses this table.</summary>
    public required IReadOnlyDictionary<string, decimal> MpgByModel { get; init; }

    public required HardFilters Filters { get; init; }

    public required IReadOnlyList<decimal> TargetMonthlyBudgets { get; init; }

    public int HoldMonths => HoldYears * 12;
}
