using System.Text.RegularExpressions;
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
        bool mpgUnknown = false,
        IReadOnlyList<string>? failureReasons = null,
        CostBreakdown? cost = null,
        string? dealerGrade = null,
        bool onlyFGraded = false,
        decimal? shippingFee = null,
        decimal? pickupFee = null,
        string? pickupLocation = null,
        Fulfillment fulfillment = Fulfillment.Delivery,
        decimal? itemizedFees = null,
        string? unmeasuredOnlyAtStore = null,
        string? priceNote = null,
        IReadOnlyList<string>? unconfirmedFeatures = null)
    {
        var vehicle = new VehicleForScoring
        {
            Vin = vin,
            Year = year,
            Make = make,
            Model = model,
            Mileage = 40000,
            LowestCurrentPrice = price,
            ShippingFee = shippingFee,
            PickupFee = pickupFee,
            PickupLocation = pickupLocation,
            Fulfillment = fulfillment,
            ItemizedFees = itemizedFees,
            DealerGrade = dealerGrade,
            OnlyFGradedDealers = onlyFGraded,
            UnmeasuredOnlyAtStore = unmeasuredOnlyAtStore,
            PriceNote = priceNote,
        };

        return new Score
        {
            Vehicle = vehicle,
            Passes = passes,
            FailureReasons = failureReasons ?? [],
            InsuranceUnknown = insuranceUnknown,
            MpgUnknown = mpgUnknown,
            Cost = cost,
            UnconfirmedFeatures = unconfirmedFeatures ?? [],
        };
    }

    /// <summary>A cost whose loan payment equals <paramref name="duringLoan"/>: nothing in this
    /// file's threshold and unmet-target tests cares about the split between the payment and the
    /// running costs riding on top of it, only about the one figure the over-budget and
    /// unmet-target comparisons key off (now the payment, not the during-loan total), so giving
    /// them the same band lets every existing "$590-$626"-style figure keep meaning what it always
    /// did. <see cref="BuildItemizedCost"/> is for the tests that do care about the split.</summary>
    private static CostBreakdown BuildCost(Band duringLoan, Band tenYearAvg)
    {
        Band point = Band.Point(100m);
        return new CostBreakdown
        {
            PurchaseCost = 20000m,
            Payment = duringLoan,
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

    private static string[] Render(IReadOnlyList<Score> scores, decimal? budget, IReadOnlyDictionary<string, ResearchStatus>? research = null, IReadOnlyList<decimal>? targets = null, bool detail = false)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var console = new TestConsole();
            console.Profile.Width = 80;
            console.Profile.Capabilities.Ansi = false;

            RankRenderer.Render(console, scores, budget, research ?? new Dictionary<string, ResearchStatus>(), targets ?? [], detail);

            return console.Output.Replace("\r\n", "\n").Split('\n');
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    [Fact]
    public void RankedVehicles_ReturnsOnlyTheRankedSectionBestFirst()
    {
        Score cheaper = BuildScore("VIN00000000000002", 2022, "Toyota", "Prius", 18000m, cost: BuildCost(Band.Point(300m), Band.Point(400m)));
        Score dearer = BuildScore("VIN00000000000001", 2021, "Toyota", "Prius", 19000m, cost: BuildCost(Band.Point(300m), Band.Point(450m)));
        Score excluded = BuildScore("VIN00000000000003", 2015, "Toyota", "Prius", 9000m, passes: false, failureReasons: ["too old"]);
        Score noInsurance = BuildScore("VIN00000000000004", 2022, "Honda", "Insight", 17000m, insuranceUnknown: true, cost: BuildCost(Band.Point(250m), Band.Point(350m)));

        IReadOnlyList<Score> ranked = RankRenderer.RankedVehicles([dearer, excluded, noInsurance, cheaper]);

        Assert.Equal(["VIN00000000000002", "VIN00000000000001"], ranked.Select(s => s.Vehicle.Vin));
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
    public void Render_VehicleWithAListingFlagButNoResearch_MarksItFlagRatherThanNotResearched()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score flagged = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost);
        Score plain = BuildScore("4T1G11AK0LU654321", 2020, "Toyota", "Corolla Hybrid", 23000m, cost: cost);
        var research = new Dictionary<string, ResearchStatus>
        {
            ["4T1G11AK0LU123456"] = new ResearchStatus(Researched: false, ResearchedAt: null, HasRedFlag: true, RecallsKnown: false, RecallCount: 0),
            ["4T1G11AK0LU654321"] = new ResearchStatus(Researched: false, ResearchedAt: null, HasRedFlag: false, RecallsKnown: false, RecallCount: 0),
        };

        string[] lines = Render([flagged, plain], budget: null, research);

        Assert.Contains(lines, line => line.Contains("$22,000") && line.Contains("flag") && line.Contains("rc -"));
        Assert.Contains(lines, line => line.Contains("$23,000") && !line.Contains("flag") && line.Contains("rc -"));
    }

    [Fact]
    public void Render_VehicleWithAPriceNote_PrintsItUnderItsRowWithoutMarkingItFlagged()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score noted = BuildScore("JTDBCMFEXS3070309", 2025, "Toyota", "Corolla Hybrid", 17289m, cost: cost, priceNote: "priced 24% below 12 similar cars (median $22,800); confirm the title");
        Score plain = BuildScore("4T1G11AK0LU654321", 2025, "Toyota", "Corolla Hybrid", 22800m, cost: cost);
        var research = new Dictionary<string, ResearchStatus>
        {
            ["JTDBCMFEXS3070309"] = new ResearchStatus(Researched: true, ResearchedAt: DateTimeOffset.UtcNow, HasRedFlag: false, RecallsKnown: true, RecallCount: 0),
        };

        string[] lines = Render([noted, plain], budget: null, research);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        int row = Array.FindIndex(lines, line => line.Contains("JTDBCMFEXS3070309"));
        string afterRow = string.Join(" ", lines.Skip(row + 1).Take(3).Select(line => line.Trim()));
        Assert.Contains("priced 24% below 12 similar cars (median $22,800); confirm the title", afterRow);
        Assert.Single(lines, line => line.Contains("confirm the title") || line.Contains("12 similar cars"));
        Assert.Contains(lines, line => line.Contains("$17,289") && line.Contains("clean") && !line.Contains("flag"));
    }

    [Fact]
    public void Render_VehicleWithAnUnknownRequiredFeature_PrintsAConfirmNoteUnderItsRowAndStillRanksIt()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score unconfirmed = BuildScore("JTDBCMFE7P3014805", 2023, "Toyota", "Corolla Hybrid", 19990m, cost: cost, unconfirmedFeatures: ["push-button start"]);
        Score confirmed = BuildScore("4T1G11AK0LU654321", 2025, "Toyota", "Corolla Hybrid", 22800m, cost: cost);

        string[] lines = Render([unconfirmed, confirmed], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("Ranked (2)"));
        int row = Array.FindIndex(lines, line => line.Contains("JTDBCMFE7P3014805"));
        string afterRow = string.Join(" ", lines.Skip(row + 1).Take(3).Select(line => line.Trim()));
        Assert.Contains("confirm push-button start", afterRow);
        Assert.Single(lines, line => line.Contains("confirm push-button start"));
    }

    [Fact]
    public void Render_VehicleExcludedForAnAbsentRequiredFeature_ListsTheReasonUnderExcluded()
    {
        Score excluded = BuildScore("JTDBCMFE7P3014805", 2023, "Toyota", "Corolla Hybrid", 19990m, passes: false, failureReasons: ["push-button start is absent (window sticker), which the scenario requires"]);

        string[] lines = Render([excluded], budget: null);

        Assert.Contains(lines, line => line.Contains("Excluded (1)"));
        // The reasons column wraps inside the table, so compare the text with the borders and padding collapsed.
        string excludedText = Regex.Replace(string.Join(" ", lines.SkipWhile(line => !line.Contains("Excluded (1)"))).Replace('│', ' '), @"\s+", " ");
        Assert.Contains("push-button start is absent (window sticker), which the scenario requires", excludedText);
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
    public void Render_PriusPrimeIsADisallowedModel_ListedAsExcludedNotMissingScenarioData()
    {
        // Toyota Prius Prime is not a candidate: a ledger row for one fails the scenario's
        // allowedModels filter, so it lands under Excluded with that reason, never under "Not
        // ranked: scenario data missing" (which is for a model that passes the filters but is
        // simply missing an insurance or mpg figure).
        Score score = BuildScore(
            "JTDACACU5T3062285", 2024, "Toyota", "Prius Prime", 28000m,
            passes: false,
            failureReasons: ["Toyota Prius Prime is not one of the scenario's target models"]);

        string[] lines = Render([score], budget: null);
        string joined = Regex.Replace(string.Join(' ', lines).Replace('│', ' ').Replace('─', ' '), @"\s+", " ");

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.StartsWith("Excluded"));
        Assert.Contains(lines, line => line.Contains("JTDACACU5T3062285"));
        Assert.Contains("Toyota Prius Prime is not one of the scenario's target models", joined);
        Assert.Contains(lines, line => line.StartsWith("Not ranked: scenario data missing (0)"));
        Assert.DoesNotContain("needs an insurance figure for Toyota Prius Prime", joined);
        Assert.DoesNotContain("needs an mpg figure for Toyota Prius Prime", joined);
    }

    [Fact]
    public void Render_MissingScenarioDataNoVehicleGraded_OmitsGradeColumnAndPrintsOneLineNote()
    {
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, insuranceUnknown: true);

        string[] lines = Render([score], budget: null);

        Assert.Contains(lines, line => line.Trim() == "Grade: no vehicle here has a CarEdge grade yet.");
        Assert.DoesNotContain(lines, line => line.Contains("Vehicle") && line.Contains("Grade"));
    }

    [Fact]
    public void Render_MissingScenarioDataAtLeastOneVehicleGraded_ShowsAGradeColumnInsteadOfTheOmissionNote()
    {
        Score graded = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, insuranceUnknown: true, dealerGrade: "A+");
        Score ungraded = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, insuranceUnknown: true);

        string[] lines = Render([graded, ungraded], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Trim() == "Grade: no vehicle here has a CarEdge grade yet.");
        Assert.Contains(lines, line => line.Contains("Grade"));
        Assert.Contains(lines, line => line.Contains("A+"));
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

    [Fact]
    public void Render_VehicleWithAnUnmeasuredOnlyAtStore_ListsItUnderItsOwnWarningHeading()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 590m, 590m), new Band(612m, 612m, 612m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, unmeasuredOnlyAtStore: "Nowhere");

        string[] lines = Render([score], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("can't yet measure"));
        Assert.Contains(lines, line => line.Contains("4T1G11AK0LU123456") && line.Contains("Nowhere"));
    }

    [Fact]
    public void Render_VehicleWithNoUnmeasuredOnlyAtStore_PrintsNoWarningHeading()
    {
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, passes: false, failureReasons: ["price too high"]);

        string[] lines = Render([score], budget: null);

        Assert.DoesNotContain(lines, line => line.Contains("can't yet measure"));
    }

    private static string UnmetTargetsText(string[] lines)
    {
        int start = Array.FindIndex(lines, line => line.StartsWith("No rankable vehicle's loan payment meets"));
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
            "No rankable vehicle's loan payment meets a target of $300, $350, or $400; the cheapest payment is $590-$626 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
        Assert.True(
            Array.FindIndex(lines, line => line.StartsWith("No rankable vehicle's loan payment meets")) < Array.FindIndex(lines, line => line.StartsWith("Ranked (")),
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

        Assert.DoesNotContain(lines, line => line.Contains("No rankable vehicle's loan payment meets"));
        Assert.DoesNotContain(lines, line => line.Contains("odo budget"));
        Assert.Contains("Ranked (1)", lines);
    }

    [Fact]
    public void Render_RankedVehicleMeetsSomeTargets_NamesOnlyTheUnmetOnes()
    {
        CostBreakdown cost = BuildCost(new Band(310m, 320m, 330m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null, targets: [300m, 350m, 400m]);

        Assert.StartsWith("No rankable vehicle's loan payment meets a target of $300;", UnmetTargetsText(lines));
        Assert.DoesNotContain(lines, line => line.Contains("$350") || line.Contains("$400"));
    }

    [Fact]
    public void Render_TargetLineIgnoresTheBudgetFlagAndUsesTheCheapestRankedVehicle()
    {
        Score cheap = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: BuildCost(new Band(410m, 420m, 430m), new Band(500m, 500m, 500m)));
        Score pricey = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: BuildCost(new Band(590m, 600m, 610m), new Band(400m, 400m, 400m)));

        string[] lines = Render([pricey, cheap], budget: 1000m, targets: [300m, 350m, 400m]);

        Assert.Equal(
            "No rankable vehicle's loan payment meets a target of $300, $350, or $400; the cheapest payment is $410-$430 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
    }

    [Fact]
    public void Render_NoRankedVehicles_PrintsNoTargetLine()
    {
        string[] lines = Render([], budget: null, targets: [300m, 350m, 400m]);

        Assert.DoesNotContain(lines, line => line.Contains("No rankable vehicle's loan payment meets"));
    }

    [Fact]
    public void Render_BudgetFlagMovesEveryVehicleOverBudget_StillPrintsTheTargetLine()
    {
        Score cheap = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: BuildCost(new Band(590m, 608m, 626m), new Band(500m, 500m, 500m)));
        Score pricey = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: BuildCost(new Band(700m, 720m, 740m), new Band(400m, 400m, 400m)));

        string[] lines = Render([pricey, cheap], budget: 400m, targets: [300m, 350m, 400m]);

        Assert.Contains(lines, line => line.StartsWith("Ranked (0)"));
        Assert.Equal(
            "No rankable vehicle's loan payment meets a target of $300, $350, or $400; the cheapest payment is $590-$626 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
    }

    [Fact]
    public void Render_BudgetFlagSplitsVehiclesIntoRankedAndOverBudget_NamesTheCheapestAcrossBoth()
    {
        Score ranked = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Prius", 17897m, cost: BuildCost(new Band(410m, 420m, 430m), new Band(500m, 500m, 500m)));
        Score overBudget = BuildScore("1HGCM82633A004352", 2019, "Honda", "Insight", 18000m, cost: BuildCost(new Band(590m, 600m, 610m), new Band(400m, 400m, 400m)));

        string[] lines = Render([overBudget, ranked], budget: 500m, targets: [300m, 350m, 400m]);

        Assert.Contains(lines, line => line.StartsWith("Ranked (1)"));
        Assert.Contains(lines, line => line.StartsWith("Over the $500 budget (1)"));
        Assert.Equal(
            "No rankable vehicle's loan payment meets a target of $300, $350, or $400; the cheapest payment is $410-$430 a month. Run odo budget for the purchase price each target allows.",
            UnmetTargetsText(lines));
    }

    private static CostBreakdown BuildItemizedCost(Band payment, Band runningCosts, Band duringLoan)
    {
        CostBreakdown cost = BuildCost(duringLoan, new Band(612m, 696m, 780m));
        return cost with { Payment = payment, AfterPayoffMonthly = runningCosts };
    }

    private static string RunningCostsText(string[] lines)
    {
        int start = Array.FindIndex(lines, line => line.StartsWith("During-loan is the loan payment"));
        if (start < 0)
        {
            return string.Empty;
        }

        int end = Array.FindIndex(lines, start, line => line.StartsWith("Ranked ("));
        return string.Join(' ', lines[start..end].Select(line => line.Trim()));
    }

    [Fact]
    public void Render_RankedVehicle_StatesAboveTheRankedSectionWhatDuringLoanIncludes()
    {
        CostBreakdown cost = BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m));
        Score score = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.StartsWith(
            "During-loan is the loan payment plus running costs of about $268-$279 a month (insurance, fuel, maintenance, reserve).",
            RunningCostsText(lines));
        Assert.Contains("--detail", RunningCostsText(lines));
    }

    [Fact]
    public void Render_RankedVehicle_DoesNotOfferTheRemainderAsThePaymentForTenYearAvg()
    {
        CostBreakdown cost = BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m));
        Score score = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string text = RunningCostsText(Render([score], budget: null));

        Assert.DoesNotContain("remainder", text);
        Assert.Contains("10yr avg spreads those costs, the down payment, the loan payments, and resale value over the hold", text);
    }

    [Fact]
    public void Render_WithDetail_DoesNotTellTheReaderToRunWithDetail()
    {
        CostBreakdown cost = BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m));
        Score score = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string text = RunningCostsText(Render([score], budget: null, detail: true));

        Assert.StartsWith("During-loan is the loan payment", text);
        Assert.DoesNotContain("--detail", text);
    }

    [Fact]
    public void Render_VehiclesWithDifferentRunningCosts_NamesTheSpanAcrossThem()
    {
        Score prius = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m)));
        Score camry = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Camry Hybrid", 22000m, cost: BuildItemizedCost(new Band(400m, 410m, 420m), new Band(278m, 284m, 289m), new Band(680m, 694m, 709m)));

        string[] lines = Render([prius, camry], budget: null);

        Assert.Contains("running costs of about $268-$289 a month", RunningCostsText(lines));
    }

    [Fact]
    public void Render_OnlyOverBudgetVehiclesRemain_StillStatesWhatDuringLoanIncludes()
    {
        CostBreakdown cost = BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m));
        Score score = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: 300m);

        Assert.Contains("running costs of about $268-$279 a month", RunningCostsText(lines));
    }

    [Fact]
    public void Render_NoRankableVehicle_PrintsNoRunningCostsLine()
    {
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, insuranceUnknown: true);

        string[] lines = Render([score], budget: null);

        Assert.DoesNotContain(lines, line => line.Contains("running costs"));
    }

    [Fact]
    public void Render_WithDetail_PrintsOnePaymentLineUnderEachRankedAndOverBudgetRowAt80Columns()
    {
        Score ranked = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m)));
        Score overBudget = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Camry Hybrid", 22000m, cost: BuildItemizedCost(new Band(400m, 410m, 420m), new Band(278m, 284m, 289m), new Band(680m, 694m, 709m)));

        string[] lines = Render([ranked, overBudget], budget: 380m, detail: true);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Contains('\u001b'));

        int rankedRow = Array.FindIndex(lines, line => line.Contains("JTDKARFU9L3124436"));
        Assert.Contains("during $590-$626", lines[rankedRow + 1]);
        Assert.Equal("loan payment $324-$348  of during $590-$626", lines[rankedRow + 2].Trim());

        int overBudgetRow = Array.FindIndex(lines, line => line.Contains("4T1G11AK0LU123456"));
        Assert.Contains("during $680-$709", lines[overBudgetRow + 1]);
        Assert.Equal("loan payment $400-$420  of during $680-$709", lines[overBudgetRow + 2].Trim());
    }

    [Fact]
    public void Render_WithoutDetail_PrintsNoPaymentLines()
    {
        CostBreakdown cost = BuildItemizedCost(new Band(324m, 336m, 348m), new Band(268m, 274m, 279m), new Band(590m, 608m, 626m));
        Score score = BuildScore("JTDKARFU9L3124436", 2020, "Toyota", "Prius", 17897m, cost: cost);

        string[] lines = Render([score], budget: null);

        Assert.DoesNotContain(lines, line => line.Contains("loan payment $"));
    }

    [Fact]
    public void Render_DetailForAVehicleWithAShippingFee_NamesTheFeeAndThePurchasePriceItAddsUpTo()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, shippingFee: 1590m);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Contains("$23,590") && line.Contains("during $590-$626"));
        int priceLine = Array.FindIndex(lines, line => line.Trim() == "price $22,000 asking + $1,590 shipping = $23,590 (delivery)");
        Assert.True(priceLine >= 0, "expected an itemized price line naming the shipping fee");
        Assert.StartsWith("    loan payment", lines[priceLine + 1]);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void Render_DetailForAVehicleWithItemizedFees_NamesTheFeesAndThePurchasePriceItAddsUpTo()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, itemizedFees: 1494m);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Contains("$23,494") && line.Contains("during $590-$626"));
        Assert.Contains(lines, line => line.Trim() == "price $22,000 asking + $1,494 fees = $23,494");
        Assert.DoesNotContain(lines, line => line.Contains("shipping"));
    }

    [Fact]
    public void Render_DetailForAVehicleWithShippingAndItemizedFees_NamesBothInOneLine()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, shippingFee: 1590m, itemizedFees: 1494m);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Trim() == "price $22,000 asking + $1,590 shipping + $1,494 fees = $25,084 (delivery)");
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void Render_DetailForAFreeShippingVehicle_StillNamesTheZeroFee()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, shippingFee: 0m);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Trim() == "price $22,000 asking + $0 shipping = $22,000 (delivery)");
    }

    [Fact]
    public void Render_DetailForAVehicleWithNoShippingFee_PrintsNoPriceLineAndTheAskingPrice()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.DoesNotContain(lines, line => line.Contains("shipping"));
        Assert.Contains(lines, line => line.Contains("$22,000") && line.Contains("during $590-$626"));
    }

    [Fact]
    public void Render_DetailUnderPickup_NamesThePickupFeeItsLocationAndThePriceWithoutTheShippingFee()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 17990m, cost: cost,
            shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL", fulfillment: Fulfillment.Pickup);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Trim() == "price $17,990 asking + $0 pickup = $17,990 (pickup at Orlando, FL)");
        Assert.Contains(lines, line => line.Contains("$17,990") && line.Contains("during $590-$626"));
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void Render_DetailUnderPickupWithNoPickupFeeRead_NamesTheShippingFeeItAssumedInItsPlace()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 17990m, cost: cost,
            shippingFee: 990m, fulfillment: Fulfillment.Pickup);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Trim() == "price $17,990 asking + $990 shipping = $18,980 (pickup, fee unknown)");
    }

    [Fact]
    public void Render_DetailUnderDeliveryWithAKnownPickupOption_NamesDeliveryAndTheShippingFee()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 17990m, cost: cost,
            shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL");

        string[] lines = Render([score], budget: null, detail: true);

        Assert.Contains(lines, line => line.Trim() == "price $17,990 asking + $990 shipping = $18,980 (delivery)");
    }

    [Fact]
    public void Render_DetailUnderPickupForAVehicleWithNoFeeAtAll_PrintsNoPriceLine()
    {
        CostBreakdown cost = BuildCost(new Band(590m, 608m, 626m), new Band(612m, 696m, 780m));
        Score score = BuildScore("4T1G11AK0LU123456", 2020, "Toyota", "Corolla Hybrid", 22000m, cost: cost, fulfillment: Fulfillment.Pickup);

        string[] lines = Render([score], budget: null, detail: true);

        Assert.DoesNotContain(lines, line => line.Trim().StartsWith("price "));
    }
}
