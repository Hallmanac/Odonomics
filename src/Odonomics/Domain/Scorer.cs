using Odonomics.Finance;

namespace Odonomics.Domain;

/// <summary>
/// Pure scoring: a vehicle plus a scenario in, a <see cref="Score"/> out. Hard filters always run
/// first and are reported with reasons regardless of insurance availability; a vehicle without a
/// current asking price (every posting gone) or without a known insurance figure for its model
/// still gets a <see cref="Score"/>, just without a <see cref="CostBreakdown"/>.
/// </summary>
public static class Scorer
{
    public static Score Score(VehicleForScoring vehicle, Scenario scenario)
    {
        List<string> reasons = [.. FilterReasons(vehicle, scenario)];
        bool passes = reasons.Count == 0;

        decimal? insuranceMonthly = scenario.InsuranceMonthlyByModel.GetValueOrDefault(vehicle.MakeModel);
        bool insuranceUnknown = insuranceMonthly is null;

        CostBreakdown? cost = passes && !insuranceUnknown && vehicle.LowestCurrentPrice is decimal price
            ? ComputeCost(price, insuranceMonthly!.Value, MpgFor(vehicle, scenario), scenario)
            : null;

        return new Score
        {
            Vehicle = vehicle,
            Passes = passes,
            FailureReasons = reasons,
            InsuranceUnknown = insuranceUnknown,
            Cost = cost,
        };
    }

    public static IReadOnlyList<string> FilterReasons(VehicleForScoring vehicle, Scenario scenario)
    {
        var reasons = new List<string>();
        string makeModel = vehicle.MakeModel;

        if (!scenario.Filters.AllowedModels.Contains(makeModel, StringComparer.OrdinalIgnoreCase))
        {
            reasons.Add($"{makeModel} is not one of the scenario's target models");
        }

        int minYear = scenario.Filters.MinYearFor(makeModel);
        if (vehicle.Year < minYear)
        {
            reasons.Add($"model year {vehicle.Year} is below the minimum {minYear} for {makeModel}");
        }

        if (vehicle.Mileage > scenario.Filters.MaxMileage)
        {
            reasons.Add($"mileage {vehicle.Mileage:N0} exceeds the maximum {scenario.Filters.MaxMileage:N0}");
        }

        // Spike finding: exclude new stock, since this scenario is shopping used. A vehicle with
        // under 500 miles or a model year beyond the current year is new inventory, not used.
        if (vehicle.Mileage < 500 || vehicle.Year > DateTime.UtcNow.Year)
        {
            reasons.Add("excluded as new stock (under 500 miles or a model year beyond the current year)");
        }

        if (vehicle.LowestCurrentPrice is null)
        {
            reasons.Add("no current asking price (every posting is gone)");
        }

        return reasons;
    }

    private static decimal MpgFor(VehicleForScoring vehicle, Scenario scenario) =>
        scenario.MpgByModel.TryGetValue(vehicle.MakeModel, out decimal mpg)
            ? mpg
            : throw new InvalidOperationException($"no mpg entry for \"{vehicle.MakeModel}\" in the scenario's mpgByModel table");

    /// <summary>
    /// The parameters that can make any cost line a band: fees and down payment (purchase cost
    /// and financed principal), APR (payment), annual miles and gas price (fuel; miles also feeds
    /// maintenance), maintenance per mile, the emergency reserve, and the residual fraction
    /// (depreciation). Held in one fixed order so <see cref="Evaluate"/> can index into the raw
    /// values array BandCalculator hands back at each corner.
    /// </summary>
    private static Parameter[] CostParameters(Scenario scenario) =>
    [
        scenario.Fees,
        scenario.DownPayment,
        scenario.Apr,
        scenario.AnnualMiles,
        scenario.GasPricePerGallon,
        scenario.MaintenancePerMile,
        scenario.EmergencyReservePerMonth,
        scenario.ResidualFraction,
    ];

    public static CostBreakdown ComputeCost(decimal price, decimal insuranceMonthly, decimal mpg, Scenario scenario)
    {
        Parameter[] parameters = CostParameters(scenario);

        EvaluatedCost EvaluateAt(IReadOnlyList<decimal> values) =>
            Evaluate(price, insuranceMonthly, mpg, scenario, values);

        Band Metric(Func<EvaluatedCost, decimal> select) =>
            BandCalculator.Compute(parameters, values => select(EvaluateAt(values)));

        EvaluatedCost expected = EvaluateAt([.. parameters.Select(p => p.Expected)]);

        return new CostBreakdown
        {
            PurchaseCost = expected.PurchaseCost,
            Payment = Metric(e => e.Payment),
            Fuel = Metric(e => e.Fuel),
            Maintenance = Metric(e => e.Maintenance),
            Reserve = Metric(e => e.Reserve),
            InsuranceMonthly = insuranceMonthly,
            Depreciation = Metric(e => e.Depreciation),
            ResidualValue = Metric(e => e.ResidualValue),
            DuringLoanMonthly = Metric(e => e.DuringLoanMonthly),
            AfterPayoffMonthly = Metric(e => e.AfterPayoffMonthly),
            TenYearTotal = Metric(e => e.TenYearTotal),
            TenYearAverageMonthly = Metric(e => e.TenYearAverageMonthly),
        };
    }

    private readonly record struct EvaluatedCost(
        decimal PurchaseCost,
        decimal Payment,
        decimal Fuel,
        decimal Maintenance,
        decimal Reserve,
        decimal Depreciation,
        decimal ResidualValue,
        decimal DuringLoanMonthly,
        decimal AfterPayoffMonthly,
        decimal TenYearTotal,
        decimal TenYearAverageMonthly);

    /// <summary>Runs the whole money-definitions chain once, for one corner of the loose
    /// parameters' box (see <see cref="CostParameters"/> for the value order).</summary>
    private static EvaluatedCost Evaluate(decimal price, decimal insuranceMonthly, decimal mpg, Scenario scenario, IReadOnlyList<decimal> values)
    {
        decimal fees = values[0];
        decimal downPayment = values[1];
        decimal apr = values[2];
        decimal annualMiles = values[3];
        decimal gasPricePerGallon = values[4];
        decimal maintenancePerMile = values[5];
        decimal reserve = values[6];
        decimal residualFraction = values[7];

        decimal salesTax = FinanceMath.FloridaSalesTax(price, scenario.SalesTaxStateRate, scenario.CountySurtaxRate);
        decimal purchaseCost = price + salesTax + fees;
        decimal principal = Math.Max(0m, purchaseCost - downPayment);
        decimal payment = FinanceMath.AmortizedPayment(principal, apr, scenario.TermMonths);
        decimal fuel = FinanceMath.MonthlyFuelCost(annualMiles, mpg, gasPricePerGallon);
        decimal maintenance = maintenancePerMile * annualMiles / 12m;
        (decimal residualValue, decimal depreciation) = FinanceMath.StraightLineDepreciation(purchaseCost, residualFraction);
        decimal duringLoan = FinanceMath.DuringLoanMonthly(payment, insuranceMonthly, fuel, maintenance, reserve);
        decimal afterPayoff = FinanceMath.AfterPayoffMonthly(insuranceMonthly, fuel, maintenance, reserve);
        decimal tenYearTotal = FinanceMath.TenYearTotal(
            downPayment, payment, scenario.TermMonths, scenario.HoldMonths, insuranceMonthly, fuel, maintenance, reserve, residualValue);
        decimal tenYearAverage = FinanceMath.TenYearAverageMonthly(tenYearTotal, scenario.HoldMonths);

        return new EvaluatedCost(
            purchaseCost, payment, fuel, maintenance, reserve, depreciation, residualValue,
            duringLoan, afterPayoff, tenYearTotal, tenYearAverage);
    }
}
