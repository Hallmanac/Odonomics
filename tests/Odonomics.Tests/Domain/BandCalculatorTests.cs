using Odonomics.Domain;
using Odonomics.Finance;

namespace Odonomics.Tests.Domain;

public class BandCalculatorTests
{
    [Fact]
    public void Compute_AllParametersPinned_ReturnsPointBand()
    {
        Parameter[] parameters = [Parameter.Pinned(20000m), Parameter.Pinned(0.06m)];

        Band band = BandCalculator.Compute(parameters, values => values[0] + values[1]);

        Assert.False(band.IsRange);
        Assert.Equal(20000.06m, band.Low);
        Assert.Equal(20000.06m, band.Expected);
        Assert.Equal(20000.06m, band.High);
    }

    // The money-definitions contract: a loose APR must yield a payment band whose low and high
    // match the pinned calculation at each end of the APR range. Principal $20,000, term 60
    // months, APR loose between 5% and 7% (midpoint 6%, the same scenario FinanceMathTests
    // proves as a pinned calculation).
    [Fact]
    public void Compute_LooseApr_BandEndsMatchPinnedCalculationAtEachEnd()
    {
        const decimal principal = 20000m;
        const int termMonths = 60;
        Parameter[] parameters = [Parameter.Pinned(principal), Parameter.Loose(0.05m, 0.07m)];

        Band band = BandCalculator.Compute(
            parameters,
            values => FinanceMath.AmortizedPayment(values[0], values[1], termMonths));

        decimal pinnedAtLow = FinanceMath.AmortizedPayment(principal, 0.05m, termMonths);
        decimal pinnedAtHigh = FinanceMath.AmortizedPayment(principal, 0.07m, termMonths);
        decimal pinnedAtMidpoint = FinanceMath.AmortizedPayment(principal, 0.06m, termMonths);

        Assert.True(band.IsRange);
        Assert.Equal(pinnedAtLow, band.Low);
        Assert.Equal(pinnedAtHigh, band.High);
        Assert.Equal(pinnedAtMidpoint, band.Expected);
    }

    [Fact]
    public void Compute_TwoLooseParameters_ReturnsBoundingBoxOverAllFourCorners()
    {
        // annualMiles loose 10,000-14,000, mpg loose 35-45; fuel cost is annualMiles/mpg*price/12,
        // which is increasing in miles and decreasing in mpg, so the true min is at
        // (miles=low, mpg=high) and the true max is at (miles=high, mpg=low) - both are corners,
        // so the bounding-box approach finds the exact answer here even with two loose inputs.
        Parameter[] parameters = [Parameter.Loose(10000m, 14000m), Parameter.Loose(35m, 45m), Parameter.Pinned(3.50m)];

        Band band = BandCalculator.Compute(parameters, values => FinanceMath.MonthlyFuelCost(values[0], values[1], values[2]));

        decimal expectedLow = FinanceMath.MonthlyFuelCost(10000m, 45m, 3.50m);
        decimal expectedHigh = FinanceMath.MonthlyFuelCost(14000m, 35m, 3.50m);

        Assert.Equal(expectedLow, band.Low);
        Assert.Equal(expectedHigh, band.High);
    }

    [Fact]
    public void LooseParameter_MaxBelowMin_Throws()
    {
        Assert.Throws<ArgumentException>(() => Parameter.Loose(10m, 5m));
    }
}
