namespace Odonomics.Finance;

/// <summary>
/// Pure cost-model functions. Every formula here is the contract proved by
/// tests/Odonomics.Tests/Finance/FinanceMathTests.cs against hand-calculated values; see the
/// money definitions in the v0 brief for the reasoning behind each one.
/// </summary>
public static class FinanceMath
{
    /// <summary>Standard amortized monthly payment: P * r / (1 - (1 + r)^-n), r the monthly rate,
    /// n the term in months. Falls back to a straight-line split when the rate is zero, since the
    /// standard formula divides by zero there.</summary>
    public static decimal AmortizedPayment(decimal principal, decimal annualPercentageRate, int termMonths)
    {
        if (termMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(termMonths), termMonths, "term must be positive");
        }

        decimal monthlyRate = annualPercentageRate / 12m;
        if (monthlyRate == 0m)
        {
            return principal / termMonths;
        }

        decimal growth = DecimalPow(1m + monthlyRate, termMonths);
        return principal * monthlyRate / (1m - 1m / growth);
    }

    /// <summary>Total interest paid over the full loan term: total of all payments minus the
    /// principal actually financed.</summary>
    public static decimal TotalInterest(decimal principal, decimal payment, int termMonths) =>
        payment * termMonths - principal;

    /// <summary>Florida sales tax: the 6% state rate on the full price, plus the county
    /// discretionary surtax on the first $5,000 of price only (Fla. Stat. 212.055).</summary>
    public static decimal FloridaSalesTax(decimal price, decimal countySurtaxRate)
    {
        const decimal stateRate = 0.06m;
        const decimal surtaxCap = 5000m;
        decimal surtaxableAmount = Math.Min(price, surtaxCap);
        return price * stateRate + surtaxableAmount * countySurtaxRate;
    }

    /// <summary>Monthly fuel cost from annual mileage, fuel economy, and price per gallon.</summary>
    public static decimal MonthlyFuelCost(decimal annualMiles, decimal milesPerGallon, decimal gasPricePerGallon) =>
        annualMiles / milesPerGallon * gasPricePerGallon / 12m;

    /// <summary>Straight-line depreciation from purchase cost to a residual fraction of that same
    /// purchase cost at the end of the hold period.</summary>
    public static (decimal ResidualValue, decimal TotalDepreciation) StraightLineDepreciation(
        decimal purchaseCost, decimal residualFraction)
    {
        decimal residual = purchaseCost * residualFraction;
        return (residual, purchaseCost - residual);
    }

    /// <summary>Monthly cost while the loan is still being paid: payment plus every running cost.</summary>
    public static decimal DuringLoanMonthly(decimal payment, decimal insurance, decimal fuel, decimal maintenance, decimal reserve) =>
        payment + insurance + fuel + maintenance + reserve;

    /// <summary>Monthly cost once the loan is paid off: the same running costs without the payment.</summary>
    public static decimal AfterPayoffMonthly(decimal insurance, decimal fuel, decimal maintenance, decimal reserve) =>
        insurance + fuel + maintenance + reserve;

    /// <summary>
    /// Total cost over the hold period: down payment, plus every loan payment actually made
    /// (principal and interest together, counted exactly once - the loan stops contributing once
    /// its term ends even if the hold period runs longer), plus every running cost for the whole
    /// hold period, minus the residual value at the end of the hold (depreciation is already
    /// inside this total through the residual, so it must never be added again on top of it).
    /// </summary>
    public static decimal TenYearTotal(
        decimal downPayment,
        decimal payment,
        int termMonths,
        int holdMonths,
        decimal insurance,
        decimal fuel,
        decimal maintenance,
        decimal reserve,
        decimal residualValue)
    {
        int monthsFinanced = Math.Min(termMonths, holdMonths);
        decimal runningCostPerMonth = insurance + fuel + maintenance + reserve;
        return downPayment + payment * monthsFinanced + runningCostPerMonth * holdMonths - residualValue;
    }

    /// <summary>Ten-year total spread evenly across the hold period, in months.</summary>
    public static decimal TenYearAverageMonthly(decimal tenYearTotal, int holdMonths) =>
        tenYearTotal / holdMonths;

    /// <summary>Integer-exponent decimal power (decimal has no built-in Pow); exact for the whole
    /// number of loan months this is always called with.</summary>
    private static decimal DecimalPow(decimal value, int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= value;
        }

        return result;
    }
}
