using Odonomics.Finance;

namespace Odonomics.Tests.Finance;

public class FinanceMathTests
{
    // Hand calculation: principal $20,000, APR 6%, 60 months.
    // monthly rate r = 0.06 / 12 = 0.005
    // payment = P * r / (1 - (1 + r)^-60)
    //         = 20000 * 0.005 / (1 - 1.005^-60)
    // 1.005^60 = 1.34885015...  =>  1.005^-60 = 0.74137220...
    // 1 - 0.74137220 = 0.25862780
    // payment = 100 / 0.25862780 = 386.6560305885...  => 386.66
    [Fact]
    public void AmortizedPayment_TwentyThousandAtSixPercentSixtyMonths_MatchesHandCalculation()
    {
        decimal payment = FinanceMath.AmortizedPayment(20000m, 0.06m, 60);

        Assert.Equal(386.66m, Math.Round(payment, 2));
    }

    // Hand calculation: a zero-interest loan is just principal split evenly across the term.
    // $12,000 over 24 months = $500.00 exactly.
    [Fact]
    public void AmortizedPayment_ZeroApr_SplitsPrincipalEvenlyAcrossTerm()
    {
        decimal payment = FinanceMath.AmortizedPayment(12000m, 0m, 24);

        Assert.Equal(500m, payment);
    }

    // Hand calculation: payment $386.656..., 60 months => total paid = $23,199.3618...
    // Interest = total paid - principal = 23199.3618... - 20000 = 3199.3618... => 3199.36
    [Fact]
    public void TotalInterest_TwentyThousandAtSixPercentSixtyMonths_MatchesHandCalculation()
    {
        decimal payment = FinanceMath.AmortizedPayment(20000m, 0.06m, 60);

        decimal interest = FinanceMath.TotalInterest(20000m, payment, 60);

        Assert.Equal(3199.36m, Math.Round(interest, 2));
    }

    // Hand calculation: price $20,000, Volusia County surtax 0.5% (0.005).
    // State: 20000 * 0.06 = 1200.00
    // Surtax: surtax applies only to the first $5,000: 5000 * 0.005 = 25.00
    // Total: 1200 + 25 = 1225.00
    [Fact]
    public void FloridaSalesTax_PriceAboveSurtaxCap_TaxesOnlyFirstFiveThousandForSurtax()
    {
        decimal tax = FinanceMath.FloridaSalesTax(20000m, 0.005m);

        Assert.Equal(1225m, tax);
    }

    // Hand calculation: price $3,000, entirely under the $5,000 surtax cap.
    // State: 3000 * 0.06 = 180.00
    // Surtax: 3000 * 0.005 = 15.00 (whole price, since it's below the cap)
    // Total: 180 + 15 = 195.00
    [Fact]
    public void FloridaSalesTax_PriceBelowSurtaxCap_TaxesWholePriceForSurtax()
    {
        decimal tax = FinanceMath.FloridaSalesTax(3000m, 0.005m);

        Assert.Equal(195m, tax);
    }

    // Hand calculation: 12,000 annual miles / 40 mpg = 300 gallons a year.
    // 300 gallons * $3.50/gallon = $1,050 a year.
    // $1,050 / 12 months = $87.50 a month.
    [Fact]
    public void MonthlyFuelCost_MatchesHandCalculation()
    {
        decimal fuel = FinanceMath.MonthlyFuelCost(12000m, 40m, 3.50m);

        Assert.Equal(87.50m, fuel);
    }

    // Hand calculation: purchase cost $21,225 (price $20,000 + tax $1,225), residual fraction 35%.
    // Residual value: 21225 * 0.35 = 7428.75
    // Total depreciation: 21225 - 7428.75 = 13796.25
    [Fact]
    public void StraightLineDepreciation_MatchesHandCalculation()
    {
        (decimal residual, decimal depreciation) = FinanceMath.StraightLineDepreciation(21225m, 0.35m);

        Assert.Equal(7428.75m, residual);
        Assert.Equal(13796.25m, depreciation);
    }

    // Hand calculation: payment 386.656..., insurance $120, fuel $87.50,
    // maintenance $0.05/mile * (12000 miles / 12 months) = $50, reserve $40.
    // During-loan monthly = 386.656... + 120 + 87.50 + 50 + 40 = 684.156... => 684.16
    [Fact]
    public void DuringLoanMonthly_MatchesHandCalculation()
    {
        decimal payment = FinanceMath.AmortizedPayment(20000m, 0.06m, 60);

        decimal duringLoan = FinanceMath.DuringLoanMonthly(payment, insurance: 120m, fuel: 87.50m, maintenance: 50m, reserve: 40m);

        Assert.Equal(684.16m, Math.Round(duringLoan, 2));
    }

    // Hand calculation: same running costs, without the payment.
    // 120 + 87.50 + 50 + 40 = 297.50
    [Fact]
    public void AfterPayoffMonthly_MatchesHandCalculation()
    {
        decimal afterPayoff = FinanceMath.AfterPayoffMonthly(insurance: 120m, fuel: 87.50m, maintenance: 50m, reserve: 40m);

        Assert.Equal(297.50m, afterPayoff);
    }

    // Hand calculation: down payment $2,000, payment $386.656... for 60 of the 120 hold months
    // (the loan pays off at month 60, so the other 60 months contribute no payment),
    // running costs $297.50/month for all 120 hold months, residual $7,428.75.
    // Ten-year total = 2000 + 386.656...*60 + 297.50*120 - 7428.75
    //                = 2000 + 23199.3618... + 35700 - 7428.75
    //                = 53470.6118... => 53470.61
    // Ten-year average = 53470.6118... / 120 = 445.5884... => 445.59
    [Fact]
    public void TenYearTotal_LoanShorterThanHold_StopsFinancingAtLoanPayoff()
    {
        decimal payment = FinanceMath.AmortizedPayment(20000m, 0.06m, 60);
        (decimal residual, _) = FinanceMath.StraightLineDepreciation(21225m, 0.35m);

        decimal tenYearTotal = FinanceMath.TenYearTotal(
            downPayment: 2000m,
            payment: payment,
            termMonths: 60,
            holdMonths: 120,
            insurance: 120m,
            fuel: 87.50m,
            maintenance: 50m,
            reserve: 40m,
            residualValue: residual);

        Assert.Equal(53470.61m, Math.Round(tenYearTotal, 2));

        decimal tenYearAverage = FinanceMath.TenYearAverageMonthly(tenYearTotal, 120);
        Assert.Equal(445.59m, Math.Round(tenYearAverage, 2));
    }

    // Hand calculation: a hold period shorter than the loan term (e.g. a hold of 24 months
    // against a 60-month loan) still only finances the months actually held, so the loan's own
    // remaining balance at hold end is not represented here (v0 does not model early-payoff
    // balances); it only proves the min(term, hold) guard actually caps at the shorter of the two.
    [Fact]
    public void TenYearTotal_HoldShorterThanLoan_CapsFinancedMonthsAtHoldLength()
    {
        decimal payment = FinanceMath.AmortizedPayment(20000m, 0.06m, 60);

        decimal total = FinanceMath.TenYearTotal(
            downPayment: 2000m,
            payment: payment,
            termMonths: 60,
            holdMonths: 24,
            insurance: 120m,
            fuel: 87.50m,
            maintenance: 50m,
            reserve: 40m,
            residualValue: 0m);

        // 2000 + 386.656...*24 + 297.50*24 - 0 = 2000 + 9279.744... + 7140 = 18419.744... => 18419.74
        Assert.Equal(18419.74m, Math.Round(total, 2));
    }
}
