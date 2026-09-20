using Odonomics.Finance;

namespace Odonomics.Domain;

/// <summary>
/// Solves the money-definitions chain in reverse: the highest purchase price whose during-loan
/// monthly cost still fits a target budget. `odo budget` is asked before a specific vehicle is
/// picked, so there is no one model's insurance or mpg to use; the caller passes averages across
/// the scenario's known models (see <see cref="ScenarioAverages"/>) unless it has a real figure.
/// During-loan monthly is non-decreasing in price (more price means at least as much tax,
/// principal, and payment), so bisection on price always converges to the highest price at or
/// under the target.
/// </summary>
public static class BudgetSolver
{
    private const decimal PriceTolerance = 0.01m;
    private const decimal SearchCeiling = 10_000_000m;

    public static decimal MaxPurchasePrice(Scenario scenario, decimal targetMonthlyBudget, decimal insuranceMonthly, decimal mpg)
    {
        decimal DuringLoanAt(decimal price)
        {
            decimal tax = FinanceMath.FloridaSalesTax(price, scenario.SalesTaxStateRate, scenario.CountySurtaxRate);
            decimal purchaseCost = price + tax + scenario.Fees.Expected;
            decimal principal = Math.Max(0m, purchaseCost - scenario.DownPayment.Expected);
            decimal payment = FinanceMath.AmortizedPayment(principal, scenario.Apr.Expected, scenario.TermMonths);
            decimal fuel = FinanceMath.MonthlyFuelCost(scenario.AnnualMiles.Expected, mpg, scenario.GasPricePerGallon.Expected);
            decimal maintenance = scenario.MaintenancePerMile.Expected * scenario.AnnualMiles.Expected / 12m;
            return FinanceMath.DuringLoanMonthly(payment, insuranceMonthly, fuel, maintenance, scenario.EmergencyReservePerMonth.Expected);
        }

        if (DuringLoanAt(0m) > targetMonthlyBudget)
        {
            return 0m;
        }

        decimal low = 0m;
        decimal high = 1000m;
        while (high < SearchCeiling && DuringLoanAt(high) <= targetMonthlyBudget)
        {
            high *= 2m;
        }

        while (high - low > PriceTolerance)
        {
            decimal mid = (low + high) / 2m;
            if (DuringLoanAt(mid) <= targetMonthlyBudget)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Round(low, 2);
    }
}

/// <summary>Model-agnostic stand-ins for <see cref="BudgetSolver"/> when no specific vehicle's
/// insurance or mpg applies yet: the average of whatever the scenario actually knows.</summary>
public static class ScenarioAverages
{
    public static decimal AverageKnownInsuranceMonthly(this Scenario scenario)
    {
        decimal[] known = [.. scenario.InsuranceMonthlyByModel.Values.Where(v => v is not null).Select(v => v!.Value)];
        return known.Length == 0 ? 0m : known.Average();
    }

    public static decimal AverageMpg(this Scenario scenario)
    {
        decimal[] mpgs = [.. scenario.MpgByModel.Values];
        return mpgs.Length == 0 ? 0m : mpgs.Average();
    }
}
