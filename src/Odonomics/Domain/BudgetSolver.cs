using Odonomics.Finance;

namespace Odonomics.Domain;

/// <summary>
/// Solves the loan half of the money-definitions chain in reverse: the highest purchase price
/// whose loan payment still fits a target monthly payment. Brian's targets
/// (<see cref="Scenario.TargetMonthlyBudgets"/>) are what he'll pay for the car itself, not the
/// running costs riding on top of it (insurance, fuel, maintenance, the emergency reserve); `odo
/// budget` prints those running costs on their own line (see <see cref="RunningCosts"/>) instead
/// of folding them into the target. A loan payment is non-decreasing in price (more price means at
/// least as much tax, principal, and payment), so bisection on price always converges to the
/// highest price at or under the target payment.
/// </summary>
public static class BudgetSolver
{
    private const decimal PriceTolerance = 0.01m;
    private const decimal SearchCeiling = 10_000_000m;

    /// <summary>The monthly figures charged before any loan payment. They do not depend on the
    /// purchase price, so the solver computes them once and `odo budget` prints the same figures,
    /// separately from the payment-based max price below (see <see cref="MaxPurchasePrice"/>).
    /// `odo budget` is asked before a specific vehicle is picked, so there is no one model's
    /// insurance or mpg to use; the caller passes averages across the scenario's known models (see
    /// <see cref="ScenarioAverages"/>) unless it has a real figure.</summary>
    public static MonthlyRunningCosts RunningCosts(Scenario scenario, decimal insuranceMonthly, decimal mpg)
    {
        decimal fuel = FinanceMath.MonthlyFuelCost(scenario.AnnualMiles.Expected, mpg, scenario.GasPricePerGallon.Expected);
        decimal maintenance = scenario.MaintenancePerMile.Expected * scenario.AnnualMiles.Expected / 12m;
        return new MonthlyRunningCosts(insuranceMonthly, fuel, maintenance, scenario.EmergencyReservePerMonth.Expected);
    }

    /// <summary>The highest purchase price whose loan payment fits the target monthly payment, as a
    /// band across the scenario's loose fees, down payment, and APR: the expected figure at every
    /// one of those three inputs' own expected value, and the low and high across the corners of
    /// whichever are loose (the same convention <see cref="Scorer.ComputeCost"/> uses via
    /// <see cref="BandCalculator"/>). In the shipped scenario only APR is loose, so the band is
    /// driven entirely by its range. It is a purchase price in the sense of
    /// <see cref="PurchasePrice.Total"/>: an asking price plus any shipping fee, the same figure
    /// `odo rank` and `odo show` price a vehicle at, so a car whose posting carries a fee has that
    /// much less room in its asking price.</summary>
    public static Band MaxPurchasePrice(Scenario scenario, decimal targetMonthlyPayment)
    {
        Parameter[] parameters = [scenario.Fees, scenario.DownPayment, scenario.Apr];
        return BandCalculator.Compute(parameters, values => MaxPriceForPaymentAt(scenario, targetMonthlyPayment, values[0], values[1], values[2]));
    }

    private static decimal MaxPriceForPaymentAt(Scenario scenario, decimal targetMonthlyPayment, decimal fees, decimal downPayment, decimal apr)
    {
        decimal PaymentAt(decimal price)
        {
            decimal tax = FinanceMath.FloridaSalesTax(price, scenario.SalesTaxStateRate, scenario.CountySurtaxRate);
            decimal purchaseCost = price + tax + fees;
            decimal principal = Math.Max(0m, purchaseCost - downPayment);
            return FinanceMath.AmortizedPayment(principal, apr, scenario.TermMonths);
        }

        if (PaymentAt(0m) > targetMonthlyPayment)
        {
            return 0m;
        }

        decimal low = 0m;
        decimal high = 1000m;
        while (high < SearchCeiling && PaymentAt(high) <= targetMonthlyPayment)
        {
            high *= 2m;
        }

        while (high - low > PriceTolerance)
        {
            decimal mid = (low + high) / 2m;
            if (PaymentAt(mid) <= targetMonthlyPayment)
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

/// <summary>Insurance, fuel, maintenance, and the emergency reserve, per month, before any loan payment.</summary>
public readonly record struct MonthlyRunningCosts(decimal Insurance, decimal Fuel, decimal Maintenance, decimal Reserve)
{
    public decimal Total => FinanceMath.AfterPayoffMonthly(Insurance, Fuel, Maintenance, Reserve);
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
        decimal[] known = [.. scenario.MpgByModel.Values.Where(v => v is not null).Select(v => v!.Value)];
        return known.Length == 0 ? 0m : known.Average();
    }
}
