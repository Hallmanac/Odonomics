using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;

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
        MpgByModel = new Dictionary<string, decimal?> { ["Honda Insight"] = 52m },
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

    [Fact]
    public void SelectVehiclesToResearch_AskingPriceUnderTheCeilingButShippingFeePushesItOver_IsExcluded()
    {
        // $24,998 alone is under the $25,000 ceiling `odo rank` would check; the $499 shipping fee
        // pushes it to $25,497. odo rank excludes this car, so odo research must too, rather than
        // spending NHTSA and Marketcheck calls on a car Brian would never buy.
        const string vin = "1HGCM82633A004354";
        VehicleEntity vehicle = Vehicle(vin);
        vehicle.Postings.Add(new PostingEntity
        {
            VehicleVin = vin,
            Source = "carmax",
            Url = "https://www.carmax.com/car/1",
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            ShippingFee = 499m,
            FeePosture = FeePostures.Itemized,
            PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 24998m, ObservedAt = DateTimeOffset.UtcNow }],
        });
        Scenario baseScenario = BuildScenario();
        Scenario scenario = baseScenario with { Filters = baseScenario.Filters with { MaxPrice = 25000 } };

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], scenario, NoCoverage);

        Assert.DoesNotContain(selected, v => v.Vin == vin);
    }

    [Fact]
    public void SelectVehiclesToResearch_OnlyAtOutOfRadiusPostingOverTheCeiling_IsExcluded()
    {
        // Before the fix, the out-of-radius "Only at" posting made IsPurchasable false, which made
        // VehicleForScoring.LowestCurrentPrice null for this vehicle; that silently skipped the price
        // ceiling check inside Scorer.FilterReasons entirely (it only runs when LowestCurrentPrice is
        // a decimal), leaving only the tolerated "out of radius" reason. This $28,000 car, over the
        // scenario's $25,000 ceiling, would have been researched anyway.
        const string vin = "1HGCM82633A004355";
        VehicleEntity vehicle = Vehicle(vin);
        vehicle.Postings.Add(new PostingEntity
        {
            VehicleVin = vin,
            Source = "carmax",
            Url = "https://www.carmax.com/car/2",
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            ShippingFee = 0m,
            PickupLocation = "Only at Norco",
            PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 28000m, ObservedAt = DateTimeOffset.UtcNow }],
        });
        Scenario baseScenario = BuildScenario();
        Scenario scenario = baseScenario with { Filters = baseScenario.Filters with { MaxPrice = 25000 } };

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], scenario, NoCoverage);

        Assert.DoesNotContain(selected, v => v.Vin == vin);
    }

    [Fact]
    public void SelectVehiclesToResearch_OnlyAtOutOfRadiusPostingUnderTheCeiling_IsStillIncluded()
    {
        const string vin = "1HGCM82633A004356";
        VehicleEntity vehicle = Vehicle(vin);
        vehicle.Postings.Add(new PostingEntity
        {
            VehicleVin = vin,
            Source = "carmax",
            Url = "https://www.carmax.com/car/3",
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            ShippingFee = 0m,
            PickupLocation = "Only at Norco",
            PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 18000m, ObservedAt = DateTimeOffset.UtcNow }],
        });
        Scenario baseScenario = BuildScenario();
        Scenario scenario = baseScenario with { Filters = baseScenario.Filters with { MaxPrice = 25000 } };

        List<VehicleEntity> selected = ResearchCommand.SelectVehiclesToResearch([vehicle], scenario, NoCoverage);

        Assert.Contains(selected, v => v.Vin == vin);
    }

    private static VinResearchResult CleanResearch(string? historyCouldNotFetchReason) => new(
        new VinDecodeResult("1HGCM82633A004352", 2020, "Honda", "Insight", null, null, null, null),
        new RecallsResult([], null),
        new ComplaintsResult(0, null),
        new SafetyRatingsResult(5, null, null, null, null, null, null),
        new VinHistoryResult([], null, historyCouldNotFetchReason));

    [Fact]
    public void IsPartiallyResearched_OnlyHistoryCouldNotFetch_ReturnsTrue()
    {
        // Pins the 2026-09-28 fix: a vehicle whose recalls, complaints, and safety ratings all came
        // back clean but whose Marketcheck VIN history hit HTTP 429 must still count as partially
        // researched, not fully researched, in odo research's summary tally.
        VinResearchResult research = CleanResearch(historyCouldNotFetchReason: "Marketcheck VIN history: HTTP 429");

        Assert.True(ResearchCommand.IsPartiallyResearched(research));
    }

    [Fact]
    public void IsPartiallyResearched_EveryPieceSucceeded_ReturnsFalse()
    {
        VinResearchResult research = CleanResearch(historyCouldNotFetchReason: null);

        Assert.False(ResearchCommand.IsPartiallyResearched(research));
    }
}
