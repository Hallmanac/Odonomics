using Odonomics.Cli;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class RankRendererRenderingTests
{
    private static Score BuildScore(
        string vin,
        int year,
        string make,
        string model,
        decimal price,
        bool passes = true,
        bool insuranceUnknown = false,
        IReadOnlyList<string>? failureReasons = null,
        CostBreakdown? cost = null,
        string? dealerGrade = null,
        bool onlyFGraded = false)
    {
        var vehicle = new VehicleForScoring
        {
            Vin = vin,
            Year = year,
            Make = make,
            Model = model,
            Mileage = 40000,
            LowestCurrentPrice = price,
            DealerGrade = dealerGrade,
            OnlyFGradedDealers = onlyFGraded,
        };

        return new Score
        {
            Vehicle = vehicle,
            Passes = passes,
            FailureReasons = failureReasons ?? [],
            InsuranceUnknown = insuranceUnknown,
            Cost = cost,
        };
    }

    private static CostBreakdown BuildCost(Band duringLoan, Band tenYearAvg)
    {
        Band point = Band.Point(100m);
        return new CostBreakdown
        {
            PurchaseCost = 20000m,
            Payment = point,
            Fuel = point,
            Maintenance = point,
            Reserve = point,
            InsuranceMonthly = 100m,
            Depreciation = point,
            ResidualValue = point,
            DuringLoanMonthly = duringLoan,
            AfterPayoffMonthly = point,
            TenYearTotal = point,
            TenYearAverageMonthly = tenYearAvg,
        };
    }

    private static string[] Render(IReadOnlyList<Score> scores, decimal? budget, IReadOnlyDictionary<string, ResearchStatus>? research = null)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var console = new TestConsole();
            console.Profile.Width = 80;
            console.Profile.Capabilities.Ansi = false;

            RankRenderer.Render(console, scores, budget, research ?? new Dictionary<string, ResearchStatus>());

            return console.Output.Replace("\r\n", "\n").Split('\n');
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    [Fact]
    public void Render_RankedCorollaHybridWithRedFlagAndTwoDigitRecalls_KeepsEveryValueUnbrokenAt80Columns()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost);
        var research = new Dictionary<string, ResearchStatus>
        {
            ["4T1G11AK0LU123456"] = new ResearchStatus(Researched: true, ResearchedAt: DateTimeOffset.UtcNow, HasRedFlag: true, RecallsKnown: true, RecallCount: 12),
        };

        string[] lines = Render([score], budget: null, research);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Contains('\u001b'));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456") && line.Contains("2020 Toyota Corolla Hybrid"));
        Assert.Contains(lines, line => line.Contains("during $590-$626"));
        Assert.Contains(lines, line => line.Contains("10yr avg $612-$780"));
        Assert.Contains(lines, line => line.Contains("flag"));
        Assert.Contains(lines, line => line.Contains("rc 12"));
        Assert.DoesNotContain(lines, line => line.Contains("red flag"));
    }

    [Fact]
    public void Render_NoVehicleGraded_OmitsGradeAndPrintsOneLineNote()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 590m, 590m), new Band(612m, 612m, 612m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost);

        string[] lines = Render([score], budget: null);

        Assert.Contains(lines, line => line.Trim() == "Grade: no vehicle here has a CarEdge grade yet.");
        Assert.DoesNotContain(lines, line => line.Contains("gr "));
    }

    [Fact]
    public void Render_AtLeastOneVehicleGraded_ShowsGradePerRowInsteadOfTheOmissionNote()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 590m, 590m), new Band(612m, 612m, 612m));
        Score graded = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, dealerGrade: "A+");
        Score ungraded = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: cost);

        string[] lines = Render([graded, ungraded], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Trim() == "Grade: no vehicle here has a CarEdge grade yet.");
        Assert.Contains(lines, line => line.Contains("gr A+"));
        Assert.Contains(lines, line => line.Contains("gr -"));
    }

    [Fact]
    public void Render_VehicleOverBudget_ListsItUnderTheBudgetHeadingWithEveryValueUnbroken()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost);

        string[] lines = Render([score], budget: 300m);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.StartsWith("Over the $300 budget"));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456") && line.Contains("2020 Toyota Corolla Hybrid"));
        Assert.Contains(lines, line => line.Contains("during $590-$626"));
        Assert.Contains(lines, line => line.Contains("10yr avg $612-$780"));
    }

    [Fact]
    public void Render_VehicleWithInsuranceUnknown_KeepsVinAndPriceUnbrokenAt80Columns()
    {
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, insuranceUnknown: true);

        string[] lines = Render([score], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456"));
        Assert.Contains(lines, line => line.Contains("$22,000"));
    }

    [Fact]
    public void Render_InsuranceUnknownVehiclesWithSameStemDifferentTrim_KeepsBothNamesDistinguishable()
    {
        Score le = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid LE", 22000m, insuranceUnknown: true);
        Score xle = BuildScore("4T1G11AK0LU654321", 2020, "Toyota", "Corolla Hybrid XLE", 23000m, insuranceUnknown: true);

        string[] lines = Render([le, xle], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("2020 Toyota Corolla Hybrid LE"));
        Assert.Contains(lines, line => line.Contains("2020 Toyota Corolla Hybrid XLE"));
    }

    [Fact]
    public void Render_DealerGradeJoinsSeveralRooftops_TruncatesRatherThanWrappingOrOverflowing()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 590m, 590m), new Band(612m, 612m, 612m));
        Score ranked = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, dealerGrade: "A+/B+/C-/D");
        Score fGraded = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: cost, dealerGrade: "A+/B+/C-/D", onlyFGraded: true);

        string[] lines = Render([ranked, fGraded], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Contains("A+/B+/C-/D"));
    }

    [Fact]
    public void Render_ExcludedVehicle_KeepsVinAndVehicleUnbrokenAt80Columns()
    {
        Score score = BuildScore(
            "4T1G11AK0LU123456",
            2020,
            "Toyota",
            "Corolla Hybrid",
            22000m,
            passes: false,
            failureReasons: ["model year below minimum", "mileage above maximum"]);

        string[] lines = Render([score], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456"));
        Assert.Contains(lines, line => line.Contains("2020 Toyota Corolla H"));
    }

    [Fact]
    public void Render_FGradedDealerOnlyVehicle_KeepsVinAndGradeUnbrokenAt80Columns()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 590m, 590m), new Band(612m, 612m, 612m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, dealerGrade: "F", onlyFGraded: true);

        string[] lines = Render([score], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("F-graded dealer only"));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456") && line.Contains("F"));
    }
}
