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

    /// <summary>Keyed by "Make Model" (e.g. "Toyota Camry Hybrid"); the model year a base model
    /// went hybrid-only, so a candidate at or above that year matches this scenario's hybrid model
    /// even when the listing text never says "Hybrid" (Toyota dropped the gas-only Camry for model
    /// year 2025). Optional: a model absent here is matched only when its own listing text says
    /// so, same as before this existed. ScenarioLoader rebuilds this dictionary with a
    /// case-insensitive comparer and validates every key is an allowed model and every value a
    /// four-digit year.</summary>
    public IReadOnlyDictionary<string, int> HybridOnlyFromModelYear { get; init; } = new Dictionary<string, int>();

    /// <summary>How the buyer takes the car home, which decides whether a listing's shipping fee or
    /// its pickup fee joins the asking price (see <see cref="PurchasePrice"/>). Optional in the scenario
    /// file, as <c>"fulfillment": "delivery"</c> or <c>"pickup"</c>; absent means delivery, so a scenario
    /// written before this existed prices exactly as it did.</summary>
    public Fulfillment Fulfillment { get; init; } = Fulfillment.Delivery;

    public required HardFilters Filters { get; init; }

    public required IReadOnlyList<decimal> TargetMonthlyBudgets { get; init; }

    public int HoldMonths => HoldYears * 12;
}
