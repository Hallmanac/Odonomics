using System.Globalization;
using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class BudgetCommandRenderingTests
{
    // Running costs: insurance averages (90 + 100) / 2 = 95; fuel is 12000 / 50 mpg * $3.20 / 12 = 64;
    // maintenance is $0.07 * 12000 / 12 = 70; reserve is 50. Together that is 279 a month.
    private static Scenario BuildScenario() => new()
    {
        Name = "test",
        Zip = "32114",
        RadiusMiles = 50,
        AnnualMiles = Parameter.Pinned(12000m),
        GasPricePerGallon = Parameter.Pinned(3.20m),
        HoldYears = 10,
        DownPayment = Parameter.Pinned(2000m),
        Apr = Parameter.Pinned(0.06m),
        TermMonths = 60,
        MaintenancePerMile = Parameter.Pinned(0.07m),
        EmergencyReservePerMonth = Parameter.Pinned(50m),
        SalesTaxStateRate = 0.06m,
        CountySurtaxRate = 0.005m,
        CountySurtaxSource = "test",
        Fees = Parameter.Pinned(500m),
        ResidualFraction = Parameter.Pinned(0.35m),
        InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Honda Insight"] = 90m, ["Toyota Prius"] = 100m },
        MpgByModel = new Dictionary<string, decimal?> { ["Honda Insight"] = 50m, ["Toyota Prius"] = 50m },
        Filters = new HardFilters
        {
            MinModelYear = 2019,
            MinModelYearOverrides = new Dictionary<string, int>(),
            MaxMileage = 100000,
            AllowedModels = ["Toyota Prius"],
        },
        TargetMonthlyBudgets = [250, 300, 400],
    };

    [Fact]
    public void Render_WithNoColorSet_PrintsTheItemizedRunningCostsAndEachTargetsMaxPriceWithinEightyColumns()
    {
        string output = RenderWithNoColor(BuildScenario());
        string[] lines = SplitLines(output);

        string prose = JoinTrimmed(lines);
        Assert.Contains(
            "about $279 a month (insurance $95, fuel $64, maintenance $70, reserve $50)",
            prose);
        Assert.Equal(2, RowCells(lines, "$250").Length);
        Assert.Equal(2, RowCells(lines, "$300").Length);
        Assert.Equal(2, RowCells(lines, "$400").Length);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void Render_PartsThatEachRoundDown_ItemizedFiguresStillAddUpToTheStatedTotal()
    {
        // Insurance averages (90 + 90.8) / 2 = 90.4 and fuel is 12000 / 50 * $3.21 / 12 = 64.2, so
        // the total is 274.6 and rounds to 275. Rounding each part alone gives 90 + 64 + 70 + 50 = 274.
        Scenario scenario = BuildScenario() with
        {
            GasPricePerGallon = Parameter.Pinned(3.21m),
            InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Honda Insight"] = 90m, ["Toyota Prius"] = 90.8m },
        };

        string prose = JoinTrimmed(SplitLines(RenderWithNoColor(scenario)));

        Assert.Contains(
            "about $275 a month (insurance $91, fuel $64, maintenance $70, reserve $50)",
            prose);
    }

    [Fact]
    public void Render_RunningCostsAreNotSubtractedFromTheTarget()
    {
        // The 250 target is below the 279 running-cost total; if the running costs were still being
        // subtracted from the target (the old behavior), that target would leave no payment room at
        // all and print a $0 max price. They no longer are, so the target itself is spent entirely on
        // the loan payment and the max price is well above zero.
        Scenario scenario = BuildScenario();

        string[] lines = SplitLines(RenderWithNoColor(scenario));
        decimal maxPriceAt250 = ParseMoney(RowCells(lines, "$250")[1]);

        Assert.True(maxPriceAt250 > 0m, $"expected a positive max price, got {maxPriceAt250}");
    }

    [Fact]
    public void Render_MaxPurchasePriceColumn_MatchesTheSolver()
    {
        Scenario scenario = BuildScenario();
        Band expected = BudgetSolver.MaxPurchasePrice(scenario, 300m);

        string[] lines = SplitLines(RenderWithNoColor(scenario));

        Assert.Equal(Format.Band(expected), RowCells(lines, "$300")[1]);
    }

    [Fact]
    public void Render_ChangingAprMovesTheMaxPriceButNotTheRunningCosts()
    {
        Scenario baseline = BuildScenario();
        Scenario pricierApr = baseline with { Apr = Parameter.Pinned(0.09m) };

        string[] baselineLines = SplitLines(RenderWithNoColor(baseline));
        string[] pricierLines = SplitLines(RenderWithNoColor(pricierApr));

        Assert.Contains("about $279 a month (insurance $95, fuel $64,", JoinTrimmed(baselineLines));
        Assert.Contains("about $279 a month (insurance $95, fuel $64,", JoinTrimmed(pricierLines));

        decimal baselinePrice = ParseMoney(RowCells(baselineLines, "$400")[1]);
        decimal pricierPrice = ParseMoney(RowCells(pricierLines, "$400")[1]);
        Assert.True(pricierPrice < baselinePrice, $"expected {pricierPrice} to be below {baselinePrice}");
    }

    [Fact]
    public void Render_AprIsALooseRange_MaxPriceColumnIsABand()
    {
        Scenario scenario = BuildScenario() with { Apr = Parameter.Loose(0.065m, 0.095m) };

        string[] lines = SplitLines(RenderWithNoColor(scenario));

        Assert.Contains('-', RowCells(lines, "$400")[1]);
    }

    [Fact]
    public void Render_ClosingLine_SaysTheMaxPurchasePriceCountsAShippingFee()
    {
        string prose = JoinTrimmed(SplitLines(RenderWithNoColor(BuildScenario())));

        Assert.Contains("Max purchase price is the asking price plus any shipping fee", prose);
    }

    [Fact]
    public void Render_UnderDelivery_ClosingLineNamesDelivery()
    {
        string prose = JoinTrimmed(SplitLines(RenderWithNoColor(BuildScenario())));

        Assert.Contains("assuming delivery", prose);
        Assert.DoesNotContain("assuming pickup", prose);
    }

    [Fact]
    public void Render_UnderPickup_ClosingLineCountsThePickupFeeAndSaysSo()
    {
        string prose = JoinTrimmed(SplitLines(RenderWithNoColor(BuildScenario() with { Fulfillment = Fulfillment.Pickup })));

        Assert.Contains("Max purchase price is the asking price plus any pickup fee", prose);
        Assert.Contains("assuming pickup", prose);
        Assert.DoesNotContain("assuming delivery", prose);
    }

    private static string JoinTrimmed(IEnumerable<string> lines) => string.Join(' ', lines.Select(line => line.Trim()));

    private static string RenderWithNoColor(Scenario scenario)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(writer) });

            BudgetCommand.Render(console, scenario);

            return writer.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    private static string[] SplitLines(string output) => output.Replace("\r\n", "\n").Split('\n');

    /// <summary>The whitespace-separated cells of the table row that starts with the target-payment
    /// figure.</summary>
    private static string[] RowCells(string[] lines, string targetCell) =>
        lines
            .Select(line => line.Split(['│', ' '], StringSplitOptions.RemoveEmptyEntries))
            .First(cells => cells.Length == 2 && cells[0] == targetCell);

    private static decimal ParseMoney(string cell) => decimal.Parse(cell.TrimStart('$'), CultureInfo.InvariantCulture);
}
