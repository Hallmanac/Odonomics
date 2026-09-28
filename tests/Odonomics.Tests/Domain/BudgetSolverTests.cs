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
        MpgByModel = new Dictionary<string, decimal?> { ["Toyota Prius"] = 40m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int>(),
            MaxMileage = 100000,
            AllowedModels = ["Toyota Prius"],
        },
        TargetMonthlyBudgets = [500],
    };

    // Hand calculation, targeting a $500 loan payment directly (running costs play no part in this
    // solver any more): payment = principal * r / (1 - (1+r)^-60), r = 0.005, the same "payment
    // factor" proven in FinanceMathTests. Solving for principal: 500 / 0.0193406... = 25862.780...
    //
    // Purchase cost = price*1.06 + surtax($25, since price will be well above the $5,000 cap) +
    // fees($500) = 1.06*price + 525. Financed principal = purchase cost - down payment($2000):
    //   25862.780... = 1.06*price + 525 - 2000
    //   price = (25862.780... + 1475) / 1.06 = 25790.358...
    //
    // $25,790.35 is the highest price, to the cent, the solver's bisection converges to for that
    // target. APR is pinned here, so the band collapses to a point.
    [Fact]
    public void MaxPurchasePrice_MatchesHandCalculation()
    {
        Scenario scenario = BuildScenario();

        Band maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyPayment: 500m);

        Assert.False(maxPrice.IsRange);
        Assert.Equal(25790.35m, maxPrice.Expected);
    }

    [Fact]
    public void MaxPurchasePrice_HigherTarget_AllowsHigherPrice()
    {
        Scenario scenario = BuildScenario();

        Band lowTargetPrice = BudgetSolver.MaxPurchasePrice(scenario, 300m);
        Band highTargetPrice = BudgetSolver.MaxPurchasePrice(scenario, 600m);

        Assert.True(highTargetPrice.Expected > lowTargetPrice.Expected);
    }

    [Fact]
    public void MaxPurchasePrice_FeesExceedTheDownPayment_AtAZeroTarget_ReturnsZero()
    {
        // With fees ($5000) above the down payment ($2000), even a free car (price 0) still leaves
        // a positive financed principal and so a positive payment, which already exceeds a $0
        // target; MaxPriceForPaymentAt's own "PaymentAt(0m) > targetMonthlyPayment" guard returns
        // zero rather than searching for a price that cannot exist.
        Scenario scenario = BuildScenario() with { Fees = Parameter.Pinned(5000m) };

        Band maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyPayment: 0m);

        Assert.Equal(0m, maxPrice.Expected);
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

    [Fact]
    public void MaxPurchasePrice_IsThePurchasePriceTotalRankPricesAVehicleAt_ShippingFeeIncluded()
    {
        Scenario scenario = BuildScenario();
        decimal maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyPayment: 500m).Expected;
        const decimal shippingFee = 1590m;

        decimal PaymentAtAsking(decimal asking) =>
            Scorer.ComputeCost(new PurchasePrice(asking, shippingFee).Total, 120m, 40m, scenario).Payment.Expected;

        // A car whose asking price plus fee is the max still fits the $500 payment target; a dollar
        // more asking does not, so the fee comes out of the asking price room one for one.
        Assert.True(PaymentAtAsking(maxPrice - shippingFee) <= 500m);
        Assert.True(PaymentAtAsking(maxPrice - shippingFee + 1m) > 500m);
    }

    // Brian's own ruling (2026-09-27): at least a 72-month loan, 3,000 to 5,000 down, and the
    // 400-a-month target is roughly the car payment. This pins the shipped scenario's own figures
    // at that target so a change to the solver, the scenario, or the shipped daughter.json's APR
    // range, term, down payment, sales tax, or fees shows up here. The band comes entirely from
    // APR (6.5% to 9.5%, the only one of fees/down payment/APR that's loose in the shipped
    // scenario): a lower APR affords a higher price (high end, $24,783.45) and a higher APR a
    // lower one (low end, $22,984.15), both well above the $7,990 a 60-month, during-loan-based
    // solve used to produce for this same target.
    [Fact]
    public void MaxPurchasePrice_ShippedDaughterScenarioAt400_MatchesThePinnedBand()
    {
        Scenario scenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

        Band maxPrice = BudgetSolver.MaxPurchasePrice(scenario, targetMonthlyPayment: 400m);

        Assert.Equal(72, scenario.TermMonths);
        Assert.Equal(3000m, scenario.DownPayment.Expected);
        Assert.Equal(22984.15m, maxPrice.Low);
        Assert.Equal(23857.36m, maxPrice.Expected);
        Assert.Equal(24783.45m, maxPrice.High);
    }
}
