using System.Globalization;
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
        MpgByModel = new Dictionary<string, decimal> { ["Honda Insight"] = 50m, ["Toyota Prius"] = 50m },
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
    public void Render_WithNoColorSet_PrintsTheItemizedRunningCostsAndEachTargetsPaymentRoomWithinEightyColumns()
    {
        string output = RenderWithNoColor(BuildScenario());
        string[] lines = SplitLines(output);

        string prose = string.Join(' ', lines.Select(line => line.Trim()));
        Assert.Contains(
            "Running costs before any payment: about $279 a month (insurance $95, fuel $64, maintenance $70, reserve $50)",
            prose);
        Assert.Equal(["$250", "$0"], RowCells(lines, "$250")[..2]);
        Assert.Equal(["$300", "$21"], RowCells(lines, "$300")[..2]);
        Assert.Equal(["$400", "$121"], RowCells(lines, "$400")[..2]);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain('\u001b', output);
    }

    [Fact]
    public void Render_MaxPurchasePriceColumn_MatchesTheSolver()
    {
        Scenario scenario = BuildScenario();
        decimal expected = BudgetSolver.MaxPurchasePrice(
            scenario, 300m, scenario.AverageKnownInsuranceMonthly(), scenario.AverageMpg());

        string[] lines = SplitLines(RenderWithNoColor(scenario));

        Assert.Equal($"${expected:N0}", RowCells(lines, "$300")[2]);
    }

    [Fact]
    public void Render_ChangingOneScenarioInput_MovesTheBlockAndTheMaxPriceTogether()
    {
        Scenario baseline = BuildScenario();
        Scenario pricierGas = baseline with { GasPricePerGallon = Parameter.Pinned(4.80m) };

        string[] baselineLines = SplitLines(RenderWithNoColor(baseline));
        string[] pricierLines = SplitLines(RenderWithNoColor(pricierGas));

        // Fuel goes from $64 to 12000 / 50 * $4.80 / 12 = $96, so the total goes from $279 to $311.
        Assert.Contains(baselineLines, line => line.Contains("about $279 a month (insurance $95, fuel $64,"));
        Assert.Contains(pricierLines, line => line.Contains("about $311 a month (insurance $95, fuel $96,"));

        decimal baselinePrice = ParseMoney(RowCells(baselineLines, "$400")[2]);
        decimal pricierPrice = ParseMoney(RowCells(pricierLines, "$400")[2]);
        Assert.True(pricierPrice < baselinePrice, $"expected {pricierPrice} to be below {baselinePrice}");
        Assert.Equal("$89", RowCells(pricierLines, "$400")[1]);
    }

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

    /// <summary>The whitespace-separated cells of the table row that starts with the budget figure.</summary>
    private static string[] RowCells(string[] lines, string budgetCell) =>
        lines
            .Select(line => line.Split(['│', ' '], StringSplitOptions.RemoveEmptyEntries))
            .First(cells => cells.Length == 3 && cells[0] == budgetCell);

    private static decimal ParseMoney(string cell) => decimal.Parse(cell.TrimStart('$'), CultureInfo.InvariantCulture);
}
