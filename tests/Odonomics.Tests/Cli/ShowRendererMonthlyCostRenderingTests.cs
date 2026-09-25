using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class ShowRendererMonthlyCostRenderingTests
{
    // Loose APR (6.5% to 9.5%) and gas price ($3.00 to $3.60) make the payment and fuel ranges. At the
    // $17,950 asking price the amount financed is $16,552 after $3,000 down, so the payment runs
    // $324 to $348; fuel is $58 to $69, and with $90 insurance, $70 maintenance and $50 reserve the
    // running costs run $268 to $279.
    internal static Scenario BuildScenario() => new()
    {
        Name = "test",
        Zip = "32114",
        RadiusMiles = 50,
        AnnualMiles = Parameter.Pinned(12000m),
        GasPricePerGallon = Parameter.Loose(3.00m, 3.60m),
        HoldYears = 10,
        DownPayment = Parameter.Pinned(3000m),
        Apr = Parameter.Loose(0.065m, 0.095m),
        TermMonths = 60,
        MaintenancePerMile = Parameter.Pinned(0.07m),
        EmergencyReservePerMonth = Parameter.Pinned(50m),
        SalesTaxStateRate = 0.06m,
        CountySurtaxRate = 0.005m,
        CountySurtaxSource = "test",
        Fees = Parameter.Pinned(500m),
        ResidualFraction = Parameter.Pinned(0.35m),
        InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Toyota Prius"] = 90m, ["Honda Insight"] = null },
        MpgByModel = new Dictionary<string, decimal> { ["Toyota Prius"] = 52m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int>(),
            MaxMileage = 100000,
            AllowedModels = ["Toyota Prius"],
        },
        TargetMonthlyBudgets = [300, 350, 400],
    };

    private static string[] Render(CostBreakdown? cost, string? unavailable = null, PurchasePrice? purchasePrice = null)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;

        ShowRenderer.RenderMonthlyCost(console, cost, unavailable, purchasePrice);

        return console.Output.Replace("\r\n", "\n").Split('\n');
    }

    private static string[] Cells(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void RenderMonthlyCost_VehicleWithACurrentPrice_ItemizesPaymentRunningCostsAndBothTotals()
    {
        Scenario scenario = BuildScenario();
        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, scenario);
        Assert.Null(unavailable);

        string[] lines = Render(cost);

        Assert.Equal("$268-$279", Format.Band(cost!.AfterPayoffMonthly));
        Assert.Equal("Monthly cost", lines[0]);
        Assert.Equal(["Loan", "payment", "$324-$348"], Cells(lines[1]));
        Assert.Equal(["Insurance", "$90"], Cells(lines[2]));
        Assert.Equal(["Fuel", "$58-$69"], Cells(lines[3]));
        Assert.Equal(["Maintenance", "$70"], Cells(lines[4]));
        Assert.Equal(["Reserve", "$50"], Cells(lines[5]));
        Assert.Equal(["During-loan", "total", "$592-$627"], Cells(lines[6]));
        Assert.Equal(["10-year", "average"], Cells(lines[7])[..2]);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void RenderMonthlyCost_TotalsAreTheSameFiguresRankUses()
    {
        Scenario scenario = BuildScenario();
        CostBreakdown ranked = Scorer.Score(
            new VehicleForScoring { Vin = "JTDKARFU9L3124436", Year = 2020, Make = "Toyota", Model = "Prius", Mileage = 40000, LowestCurrentPrice = 17950m },
            scenario).Cost!;
        (CostBreakdown? shown, _) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, scenario);

        string[] lines = Render(shown);

        Assert.Equal(ranked, shown);
        Assert.Equal(Format.Band(ranked.DuringLoanMonthly), Cells(lines[6])[^1]);
        Assert.Equal(Format.Band(ranked.TenYearAverageMonthly), Cells(lines[7])[^1]);
    }

    // At a $12,411 asking price the raw high ends are payment $224.31 and fuel $69.23, so rounding
    // each line on its own gives high ends ($224, $90, $69, $70, $50) that sum to $503 against a
    // total of $503.54, which prints as $504.
    [Theory]
    [InlineData(12411)]
    [InlineData(12907)]
    [InlineData(15333)]
    [InlineData(19999)]
    [InlineData(24187)]
    public void RenderMonthlyCost_ItemizedLinesAddUpToTheDuringLoanTotal(int price)
    {
        (CostBreakdown? cost, _) = ShowCommand.MonthlyCostFor("Toyota Prius", price, BuildScenario());

        string[] lines = Render(cost);

        (int Low, int High) Figures(string line)
        {
            int[] amounts = [.. Cells(line)[^1].Split('-').Select(figure => int.Parse(figure.TrimStart('$').Replace(",", "")))];
            return (amounts[0], amounts[^1]);
        }

        (int Low, int High)[] items = [.. lines.Skip(1).Take(5).Select(Figures)];
        (int Low, int High) total = Figures(lines[6]);

        Assert.Equal(total.Low, items.Sum(item => item.Low));
        Assert.Equal(total.High, items.Sum(item => item.High));
        Assert.Equal(Format.Band(cost!.DuringLoanMonthly), Cells(lines[6])[^1]);
    }

    [Fact]
    public void RenderMonthlyCost_PinnedScenario_ShowsEachFigureAsOnePoint()
    {
        Scenario scenario = BuildScenario() with { Apr = Parameter.Pinned(0.08m), GasPricePerGallon = Parameter.Pinned(3.30m) };
        (CostBreakdown? cost, _) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, scenario);

        string[] lines = Render(cost);

        Assert.All(lines.Skip(1).Take(7), line => Assert.DoesNotContain('-', string.Join(' ', Cells(line).Where(c => c.StartsWith('$')))));
    }

    [Fact]
    public void MonthlyCostFor_NoCurrentPrice_ExplainsInsteadOfPricing()
    {
        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor("Toyota Prius", null, BuildScenario());

        Assert.Null(cost);
        Assert.Contains("no current asking price", Render(cost, unavailable)[1]);
    }

    [Theory]
    [InlineData("Honda Insight", "insurance")]
    [InlineData("Ford Focus", "insurance")]
    public void MonthlyCostFor_ModelTheScenarioCannotPrice_ExplainsInsteadOfThrowing(string makeModel, string missing)
    {
        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor(makeModel, 17950m, BuildScenario());

        Assert.Null(cost);
        Assert.Contains(missing, unavailable);
        Assert.Contains(makeModel, unavailable);
    }

    [Fact]
    public void MonthlyCostFor_ModelWithInsuranceButNoMpg_ExplainsInsteadOfThrowing()
    {
        Scenario scenario = BuildScenario() with { MpgByModel = new Dictionary<string, decimal>() };

        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, scenario);

        Assert.Null(cost);
        Assert.Contains("mpg", unavailable);
    }

    [Fact]
    public void MonthlyCostFor_VehicleTheFiltersWouldExclude_IsStillPriced()
    {
        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, BuildScenario());

        Assert.NotNull(cost);
        Assert.Null(unavailable);
    }

    [Fact]
    public void RenderMonthlyCost_VehicleWithAShippingFee_PrintsTheFeeOnItsOwnLineUnderTheAskingPrice()
    {
        var purchasePrice = new PurchasePrice(16360m, 1590m);
        (CostBreakdown? cost, _) = ShowCommand.MonthlyCostFor("Toyota Prius", purchasePrice.Total, BuildScenario());

        string[] lines = Render(cost, purchasePrice: purchasePrice);

        Assert.Equal("Monthly cost", lines[0]);
        Assert.Equal(["Asking", "price", "$16,360"], Cells(lines[1]));
        Assert.Equal(["Shipping", "fee", "$1,590"], Cells(lines[2]));
        Assert.Equal(["Purchase", "price", "$17,950"], Cells(lines[3]));
        Assert.Equal(["Loan", "payment", "$324-$348"], Cells(lines[4]));
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void RenderMonthlyCost_FeeInThePurchasePrice_CostsTheSameAsAskingThatMuchMoreWithNoFee()
    {
        Scenario scenario = BuildScenario();
        (CostBreakdown? withFee, _) = ShowCommand.MonthlyCostFor("Toyota Prius", new PurchasePrice(16360m, 1590m).Total, scenario);
        (CostBreakdown? asked, _) = ShowCommand.MonthlyCostFor("Toyota Prius", 17950m, scenario);

        Assert.Equal(asked, withFee);
    }

    [Fact]
    public void RenderMonthlyCost_ItemizedLinesStillAddUpToTheDuringLoanTotalWithAFee()
    {
        var purchasePrice = new PurchasePrice(10821m, 1590m);
        (CostBreakdown? cost, _) = ShowCommand.MonthlyCostFor("Toyota Prius", purchasePrice.Total, BuildScenario());

        string[] lines = Render(cost, purchasePrice: purchasePrice);

        (int Low, int High) Figures(string line)
        {
            int[] amounts = [.. Cells(line)[^1].Split('-').Select(figure => int.Parse(figure.TrimStart('$').Replace(",", "")))];
            return (amounts[0], amounts[^1]);
        }

        (int Low, int High)[] items = [.. lines.Skip(4).Take(5).Select(Figures)];
        (int Low, int High) total = Figures(lines[9]);

        Assert.Equal(total.Low, items.Sum(item => item.Low));
        Assert.Equal(total.High, items.Sum(item => item.High));
    }

    [Fact]
    public void RenderMonthlyCost_VehicleWithNoShippingFee_PrintsNoPriceLines()
    {
        var purchasePrice = new PurchasePrice(17950m, ShippingFee: null);
        (CostBreakdown? cost, _) = ShowCommand.MonthlyCostFor("Toyota Prius", purchasePrice.Total, BuildScenario());

        string[] withPrice = Render(cost, purchasePrice: purchasePrice);
        string[] withoutPrice = Render(cost);

        Assert.Equal(withoutPrice, withPrice);
        Assert.Equal(["Loan", "payment", "$324-$348"], Cells(withPrice[1]));
    }
}
