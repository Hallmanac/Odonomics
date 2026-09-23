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
        decimal? price,
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

    private static string[] Render(IReadOnlyList<Score> scores, decimal? budget, IReadOnlyDictionary<string, ResearchStatus>? research = null, IReadOnlyList<decimal>? targets = null)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var console = new TestConsole();
            console.Profile.Width = 80;
            console.Profile.Capabilities.Ansi = false;

            RankRenderer.Render(console, scores, budget, research ?? new Dictionary<string, ResearchStatus>(), targets ?? []);

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

    [Fact]
    public void Render_FGradedDealerOnlyVehicleWithNoCurrentPrice_ShowsADashInsteadOfZeroDollars()
    {
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", price: null, dealerGrade: "F", onlyFGraded: true);

        string[] lines = Render([score], budget: null);

        string row = Assert.Single(lines, line => line.Contains("4T1G11AK0LU123456"));
        Assert.DoesNotContain("$0", row);
        Assert.Contains(" - ", row);
    }

    private static string UnmetTargetsText(string[] lines)
    {
        int start = Array.FindIndex(lines, line => line.StartsWith("No ranked vehicle meets"));
        if (start < 0)
        {
            return string.Empty;
        }

        int end = Array.FindIndex(lines, start, line => line.StartsWith("Ranked ("));
        return string.Join(' ', lines[start..end].Select(line => line.Trim()));
    }

    [Fact]
    public void Render_NoRankedVehicleMeetsAnyTarget_PrintsOneLineAboveRankedNamingTargetsAndCheapest()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null, targets: [300m, 350m, 400m]);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Equal(
            "No ranked vehicle meets a target budget of $300, $350, or $400 during the loan; the cheapest is $590-$626 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
        Assert.True(
            Array.FindIndex(lines, line => line.StartsWith("No ranked vehicle meets")) < Array.FindIndex(lines, line => line.StartsWith("Ranked (")),
            "the line should print above the Ranked heading");
    }

    [Fact]
    public void Render_NoRankedVehicleMeetsAnyTarget_NeverBreaksADollarFigureAcrossLines()
    {
        CostBreakdown cost = BuildCost(new Band(1590m, 1608m, 1626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: cost);

        // Each extra leading target shifts the rest of the sentence by six columns, so the band
        // and the target figures take turns landing on the 80-column wrap boundary.
        for (int extraTargets = 0; extraTargets < 10; extraTargets++)
        {
            List<decimal> targets = [.. Enumerable.Range(0, extraTargets).Select(i => 100m + i), 300m, 350m, 400m];

            string[] lines = Render([score], budget: null, targets: targets);

            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            foreach (string figure in new[] { "$1,590-$1,626" }.Concat(targets.Select(t => $"${t:N0}")))
            {
                Assert.Contains(lines, line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.TrimEnd(',', ';', '.') == figure));
            }
        }
    }

    [Fact]
    public void Render_RankedVehicleMeetsEveryTarget_PrintsNoTargetLine()
    {
        CostBreakdown cost = BuildCost(new Band(250m, 260m, 270m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null, targets: [300m, 350m, 400m]);

        Assert.DoesNotContain(lines, line => line.Contains("No ranked vehicle meets"));
        Assert.DoesNotContain(lines, line => line.Contains("odo budget"));
        Assert.Equal("Ranked (1)", lines[0]);
    }

    [Fact]
    public void Render_RankedVehicleMeetsSomeTargets_NamesOnlyTheUnmetOnes()
    {
        CostBreakdown cost = BuildCost(new Band(310m, 320m, 330m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null, targets: [300m, 350m, 400m]);

        Assert.StartsWith("No ranked vehicle meets a target budget of $300 during the loan;", UnmetTargetsText(lines));
        Assert.DoesNotContain(lines, line => line.Contains("$350") || line.Contains("$400"));
    }

    [Fact]
    public void Render_TargetLineIgnoresTheBudgetFlagAndUsesTheCheapestRankedVehicle()
    {
        Score cheap = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: BuildCost(new Band(410m, 420m, 430m), new Band(500m, 500m, 500m)));
        Score pricey = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: BuildCost(new Band(590m, 600m, 610m), new Band(400m, 400m, 400m)));

        string[] lines = Render([pricey, cheap], budget: 1000m, targets: [300m, 350m, 400m]);

        Assert.Equal(
            "No ranked vehicle meets a target budget of $300, $350, or $400 during the loan; the cheapest is $410-$430 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
    }

    [Fact]
    public void Render_NoRankedVehicles_PrintsNoTargetLine()
    {
        string[] lines = Render([], budget: null, targets: [300m, 350m, 400m]);

        Assert.DoesNotContain(lines, line => line.Contains("No ranked vehicle meets"));
    }

    [Fact]
    public void Render_BudgetFlagMovesEveryVehicleOverBudget_StillPrintsTheTargetLine()
    {
        Score cheap = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: BuildCost(new Band(590m, 608m, 626m), new Band(500m, 500m, 500m)));
        Score pricey = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: BuildCost(new Band(700m, 720m, 740m), new Band(400m, 400m, 400m)));

        string[] lines = Render([pricey, cheap], budget: 400m, targets: [300m, 350m, 400m]);

        Assert.Contains(lines, line => line.StartsWith("Ranked (0)"));
        Assert.Equal(
            "No ranked vehicle meets a target budget of $300, $350, or $400 during the loan; the cheapest is $590-$626 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
    }
}
