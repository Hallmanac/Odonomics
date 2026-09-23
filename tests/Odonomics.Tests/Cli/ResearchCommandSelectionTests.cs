using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

public class ResearchCommandSelectionTests
{
    private static readonly Dictionary<string, DateTimeOffset> NoCoverage = [];

    private static Scenario BuildScenario() => new()
    {
        Name = "test",
        Zip = "32114",
        RadiusMiles = 50,
        AnnualMiles = Parameter.Pinned(12000m),
        GasPricePerGallon = Parameter.Pinned(3.50m),
        HoldYears = 10,
        DownPayment = Parameter.Pinned(2000m),
        Apr = Parameter.Pinned(0.06m),
        TermMonths = 60,
        MaintenancePerMile = Parameter.Pinned(0.05m),
        EmergencyReservePerMonth = Parameter.Pinned(40m),
        SalesTaxStateRate = 0.06m,
        CountySurtaxRate = 0.005m,
        CountySurtaxSource = "test",
        Fees = Parameter.Pinned(0m),
        ResidualFraction = Parameter.Pinned(0.35m),
        InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Honda Insight"] = 120m },
        MpgByModel = new Dictionary<string, decimal> { ["Honda Insight"] = 52m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int>(),
            MaxMileage = 100000,
            AllowedModels = ["Honda Insight"],
        },
        TargetMonthlyBudgets = [300, 400],
    };

    private static VehicleEntity Vehicle(string vin, VinRecordEntity? vinRecord = null) => new()
    {
        Vin = vin,
        Year = 2020,
        Make = "Honda",
        Model = "Insight",
        Mileage = 40000,
        FirstSeen = DateTimeOffset.UtcNow,
        LastSeen = DateTimeOffset.UtcNow,
        VinRecord = vinRecord,
    };

    private static VinRecordEntity FreshCachedRecord(string vin) => new()
    {
        Vin = vin,
        DecodedAt = DateTimeOffset.UtcNow,
        DecodeRawJson = "",
        ResearchedAt = DateTimeOffset.UtcNow.AddDays(-1),
        HistoryRawJson = "[]",
    };

    [Fact]
    public void SelectVehiclesToResearch_VehicleWithFreshCachedRecordThatPassesFilters_IsStillIncluded()
    {
        // This is the whole point of the fix this test guards: a vehicle that needs no refresh at all
        // (VinResearchService.NeedsRefresh would return false for it) must still be selected here, so
        // the research loop gets a chance to add it to the summary marked "cached". Filtering it out
        // in this method too, the way `odo research` used to, would silently drop it from the summary
        // and would still pass every other test in this diff, since none of them exercise this seam.
        const string vin = "1HGCM82633A004352";
        VehicleEntity vehicle = Vehicle(vin, FreshCachedRecord(vin));

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], BuildScenario(), NoCoverage);

        Assert.Contains(selected, v => v.Vin == vin);
    }

    [Fact]
    public void SelectVehiclesToResearch_VehicleFailingTheScenariosModelFilter_IsExcluded()
    {
        VehicleEntity vehicle = new()
        {
            Vin = "1FADP3F20JL123456",
            Year = 2020,
            Make = "Ford",
            Model = "Focus",
            Mileage = 40000,
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
        };

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], BuildScenario(), NoCoverage);

        Assert.Empty(selected);
    }

    [Fact]
    public void SelectVehiclesToResearch_VehicleWithNoCurrentPostings_IsStillIncluded()
    {
        // A vehicle with no active posting has no current asking price; PassesScenarioFilters
        // tolerates that reason alone (see its own doc comment), so this is still eligible.
        VehicleEntity vehicle = Vehicle("1HGCM82633A004353");

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], BuildScenario(), NoCoverage);

        Assert.Contains(selected, v => v.Vin == vehicle.Vin);
    }
}
