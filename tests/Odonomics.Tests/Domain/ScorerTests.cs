using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class ScorerTests
{
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
        InsuranceMonthlyByModel = new Dictionary<string, decimal?>
        {
            ["Toyota Prius"] = 120m,
            ["Honda Insight"] = null,
        },
        MpgByModel = new Dictionary<string, decimal> { ["Toyota Prius"] = 52m, ["Honda Insight"] = 52m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int> { ["Toyota Camry Hybrid"] = 2018 },
            MaxMileage = 100000,
            AllowedModels = ["Toyota Prius", "Honda Insight", "Toyota Camry Hybrid"],
        },
        TargetMonthlyBudgets = [300, 400],
    };

    private static VehicleForScoring Vehicle(string make, string model, int year, int mileage, decimal? price) => new()
    {
        Vin = "1HGCM82633A004352",
        Make = make,
        Model = model,
        Year = year,
        Mileage = mileage,
        LowestCurrentPrice = price,
    };

    [Fact]
    public void FilterReasons_VehiclePassesEveryFilter_ReturnsNoReasons()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Empty(reasons);
    }

    [Fact]
    public void FilterReasons_ModelYearBelowMinimum_ReportsReason()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2018, 40000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("model year"));
    }

    [Fact]
    public void FilterReasons_CamryHybrid2018_AllowedByOverride()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Camry Hybrid", 2018, 40000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Empty(reasons);
    }

    [Fact]
    public void FilterReasons_MileageAboveMaximum_ReportsReason()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 150000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("mileage"));
    }

    [Fact]
    public void FilterReasons_NotATargetModel_ReportsReason()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Ford", "Focus", 2020, 40000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("not one of the scenario's target models"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(0)]
    public void FilterReasons_UnderFiveHundredMiles_ExcludedAsNewStock(int mileage)
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, mileage, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("new stock"));
    }

    [Fact]
    public void FilterReasons_ModelYearBeyondCurrentYear_ExcludedAsNewStock()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", DateTime.UtcNow.Year + 1, 40000, 18000m);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("new stock"));
    }

    [Fact]
    public void FilterReasons_NoCurrentPrice_ReportsGoneReason()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, price: null);

        IReadOnlyList<string> reasons = Scorer.FilterReasons(vehicle, scenario);

        Assert.Contains(reasons, r => r.Contains("gone"));
    }

    [Fact]
    public void Score_InsuranceUnknownForModel_ReturnsNoCostBreakdownButStillPasses()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Honda", "Insight", 2020, 40000, 15000m);

        Score score = Scorer.Score(vehicle, scenario);

        Assert.True(score.Passes);
        Assert.True(score.InsuranceUnknown);
        Assert.Null(score.Cost);
    }

    [Fact]
    public void Score_PassesWithKnownInsurance_ComputesCostBreakdown()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, 18000m);

        Score score = Scorer.Score(vehicle, scenario);

        Assert.True(score.Passes);
        Assert.False(score.InsuranceUnknown);
        Assert.NotNull(score.Cost);
        Assert.True(score.Cost.DuringLoanMonthly.Expected > 0m);
        // pinned scenario: no band, every line is a point.
        Assert.False(score.Cost.DuringLoanMonthly.IsRange);
        Assert.False(score.Cost.TenYearAverageMonthly.IsRange);
    }

    [Fact]
    public void Score_LooseApr_ProducesBandedDuringLoanMonthly()
    {
        Scenario scenario = BuildScenario() with { Apr = Parameter.Loose(0.05m, 0.07m) };
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, 18000m);

        Score score = Scorer.Score(vehicle, scenario);

        Assert.NotNull(score.Cost);
        Assert.True(score.Cost.DuringLoanMonthly.IsRange);
        Assert.True(score.Cost.DuringLoanMonthly.Low < score.Cost.DuringLoanMonthly.High);
        Assert.True(score.Cost.TenYearAverageMonthly.IsRange);
    }

    [Fact]
    public void Score_FailedFilterHasNoCostBreakdown()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2018, 40000, 18000m);

        Score score = Scorer.Score(vehicle, scenario);

        Assert.False(score.Passes);
        Assert.Null(score.Cost);
    }

    [Fact]
    public void Score_VehicleWithAShippingFee_IsCostedAtTheAskingPricePlusTheFee()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring shipped = Vehicle("Toyota", "Prius", 2020, 40000, 16410m) with { ShippingFee = 1590m };
        VehicleForScoring pickedUp = Vehicle("Toyota", "Prius", 2020, 40000, 18000m);

        Score shippedScore = Scorer.Score(shipped, scenario);

        Assert.Equal(18000m, shipped.PurchasePrice?.Total);
        Assert.Equal(Scorer.Score(pickedUp, scenario).Cost, shippedScore.Cost);
    }

    [Fact]
    public void Score_VehicleWithNoShippingFee_IsCostedAtItsAskingPrice()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, 18000m);

        Assert.Equal(18000m, vehicle.PurchasePrice?.Total);
        Assert.Equal(Scorer.ComputeCost(18000m, 120m, 52m, scenario), Scorer.Score(vehicle, scenario).Cost);
    }

    [Fact]
    public void Score_FreeShippingCostsTheSameAsNoFee()
    {
        Scenario scenario = BuildScenario();
        VehicleForScoring free = Vehicle("Toyota", "Prius", 2020, 40000, 18000m) with { ShippingFee = 0m };

        Assert.Equal(Scorer.Score(Vehicle("Toyota", "Prius", 2020, 40000, 18000m), scenario).Cost, Scorer.Score(free, scenario).Cost);
    }

    [Fact]
    public void PurchasePrice_VehicleWithNoAskingPrice_IsNullEvenWithAFee()
    {
        VehicleForScoring vehicle = Vehicle("Toyota", "Prius", 2020, 40000, null) with { ShippingFee = 1590m };

        Assert.Null(vehicle.PurchasePrice);
    }
}
