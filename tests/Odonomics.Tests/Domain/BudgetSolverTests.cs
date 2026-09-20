using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class BudgetSolverTests
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
        Fees = Parameter.Pinned(500m),
        ResidualFraction = Parameter.Pinned(0.35m),
        InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Toyota Prius"] = 120m },
        MpgByModel = new Dictionary<string, decimal> { ["Toyota Prius"] = 40m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int>(),
            MaxMileage = 100000,
            AllowedModels = ["Toyota Prius"],
        },
        TargetMonthlyBudgets = [500],
    };

    // Hand calculation: mpg 40, gas $3.50, annual miles 12000 => fuel $87.50/month (see
    // FinanceMathTests). Maintenance $0.05/mile * 12000/12 = $50/month. Insurance $120/month.
    // Reserve $40/month. Those four running costs sum to 297.50, so the payment must be
    // 500 - 297.50 = 202.50 to hit a $500 during-loan budget exactly.
    //
    // Payment = principal * r / (1 - (1+r)^-60), r = 0.005: the same "payment factor" proven in
    // FinanceMathTests (386.656.../20000 = 0.019332801...). Solving for principal:
    //   principal = 202.50 / 0.019332801... = 10474.426...
    //
    // Purchase cost = price*1.06 + surtax($25, since price will be well above the $5,000 cap) +
    // fees($500) = 1.06*price + 525. Financed principal = purchase cost - down payment($2000):
    //   10474.426... = 1.06*price + 525 - 2000
    //   price = (10474.426... + 1475) / 1.06 = 11273.043...
    //
    // At $11,273.04 during-loan monthly is 499.99989... (fits); at $11,273.05 it is 500.00013...
    // (does not), so $11,273.04 is the highest price, to the cent, that still fits.
    [Fact]
    public void MaxPurchasePrice_MatchesHandCalculation()
    {
        Scenario scenario = BuildScenario();

        decimal maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyBudget: 500m, insuranceMonthly: 120m, mpg: 40m);

        Assert.Equal(11273.04m, maxPrice);
    }

    [Fact]
    public void MaxPurchasePrice_HigherBudget_AllowsHigherPrice()
    {
        Scenario scenario = BuildScenario();

        decimal lowBudgetPrice = BudgetSolver.MaxPurchasePrice(scenario, 400m, insuranceMonthly: 120m, mpg: 40m);
        decimal highBudgetPrice = BudgetSolver.MaxPurchasePrice(scenario, 600m, insuranceMonthly: 120m, mpg: 40m);

        Assert.True(highBudgetPrice > lowBudgetPrice);
    }

    [Fact]
    public void MaxPurchasePrice_BudgetBelowFixedRunningCosts_ReturnsZero()
    {
        Scenario scenario = BuildScenario();

        // Running costs alone (297.50) already exceed a 200 budget, so even a free car (price 0,
        // financed principal negative and clamped to zero, so payment is zero) cannot fit.
        decimal maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyBudget: 200m, insuranceMonthly: 120m, mpg: 40m);

        Assert.Equal(0m, maxPrice);
    }

    [Fact]
    public void AverageKnownInsuranceMonthly_IgnoresNullEntries()
    {
        Scenario scenario = BuildScenario() with
        {
            InsuranceMonthlyByModel = new Dictionary<string, decimal?>
            {
                ["Toyota Prius"] = 90m,
                ["Honda Insight"] = 110m,
                ["Toyota Camry Hybrid"] = null,
            },
        };

        decimal average = scenario.AverageKnownInsuranceMonthly();

        Assert.Equal(100m, average);
    }
}
