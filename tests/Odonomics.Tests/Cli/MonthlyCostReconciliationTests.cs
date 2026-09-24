using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class MonthlyCostReconciliationTests
{
    private static Scenario WithCents(decimal insurance, decimal reserve, decimal maxGasPrice) =>
        ShowRendererMonthlyCostRenderingTests.BuildScenario() with
        {
            InsuranceMonthlyByModel = new Dictionary<string, decimal?> { ["Toyota Prius"] = insurance },
            EmergencyReservePerMonth = Parameter.Pinned(reserve),
            GasPricePerGallon = Parameter.Loose(3.00m, maxGasPrice),
        };

    private static Scenario Shipped() => ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static CostBreakdown CostAt(Scenario scenario, decimal price)
    {
        (CostBreakdown? cost, string? unavailable) = ShowCommand.MonthlyCostFor("Toyota Prius", price, scenario);
        Assert.Null(unavailable);
        return cost!;
    }

    private static Score ScoreAt(Scenario scenario, decimal price) => Scorer.Score(
        new VehicleForScoring { Vin = "JTDKARFU9L3124436", Year = 2020, Make = "Toyota", Model = "Prius", Mileage = 40000, LowestCurrentPrice = price },
        scenario);

    private static void AssertBothSidesAddUp(CostBreakdown cost, string context)
    {
        IReadOnlyList<RoundedLine> lines = Format.DuringLoanLines(cost);

        Assert.True(
            lines.Sum(l => l.Low) == Math.Round(cost.DuringLoanMonthly.Low, MidpointRounding.AwayFromZero),
            $"{context}: low ends {string.Join("/", lines.Select(l => l.Low))} do not add up to {Format.Money(cost.DuringLoanMonthly.Low)}");
        Assert.True(
            lines.Sum(l => l.High) == Math.Round(cost.DuringLoanMonthly.High, MidpointRounding.AwayFromZero),
            $"{context}: high ends {string.Join("/", lines.Select(l => l.High))} do not add up to {Format.Money(cost.DuringLoanMonthly.High)}");
        Assert.All(lines.Where(l => !l.IsRange), l => Assert.Equal(l.Low, l.High));
    }

    // Insurance $80.64 and reserve $50.60 both carry cents, so the fixed lines' floors are $1.24
    // short of their true sum. Reconciling the high ends against the already-rounded fixed lines
    // printed high ends adding to $482 under a during-loan high of $481.
    [Fact]
    public void DuringLoanLines_TwoFixedLinesWithCentsAt11813_HighEndsAddUpToTheTotal()
    {
        CostBreakdown cost = CostAt(WithCents(80.64m, 50.60m, 3.60m), 11813m);

        AssertBothSidesAddUp(cost, "$11,813");
        Assert.Equal("$456-$481", Format.Band(cost.DuringLoanMonthly));
    }

    // The mirror case: the same reconciliation printed high ends adding to $525 under a high of $526.
    [Fact]
    public void DuringLoanLines_TwoFixedLinesWithCentsAt13609_HighEndsAddUpToTheTotal()
    {
        CostBreakdown cost = CostAt(WithCents(80.30m, 47.50m, 3.99m), 13609m);

        AssertBothSidesAddUp(cost, "$13,609");
        Assert.Equal("$526", Format.Money(cost.DuringLoanMonthly.High));
    }

    [Fact]
    public void DuringLoanLines_SweepOfPricesAndFractionalScenarios_BothSidesAlwaysAddUp()
    {
        (decimal Insurance, decimal Reserve, decimal MaxGas)[] shapes =
        [
            (90m, 50m, 3.60m),
            (80.64m, 50.60m, 3.60m),
            (80.30m, 47.50m, 3.99m),
            (95.50m, 50m, 3.60m),
            (99.99m, 49.99m, 3.33m),
        ];

        foreach ((decimal insurance, decimal reserve, decimal maxGas) in shapes)
        {
            Scenario scenario = WithCents(insurance, reserve, maxGas);
            for (decimal price = 6000m; price <= 35000m; price += 13m)
            {
                AssertBothSidesAddUp(CostAt(scenario, price), $"insurance {insurance}, reserve {reserve}, gas {maxGas}, price {price}");
            }
        }
    }

    [Fact]
    public void DuringLoanLines_ScenarioWithNoLooseInput_PrintsEachLineAsOnePointThatAddsUp()
    {
        Scenario scenario = WithCents(80.64m, 50.60m, 3.60m) with
        {
            Apr = Parameter.Pinned(0.08m),
            GasPricePerGallon = Parameter.Pinned(3.30m),
        };

        for (decimal price = 8000m; price <= 30000m; price += 29m)
        {
            CostBreakdown cost = CostAt(scenario, price);
            IReadOnlyList<RoundedLine> lines = Format.DuringLoanLines(cost);

            Assert.All(lines, l => Assert.DoesNotContain('-', l.Figure));
            Assert.Equal(Math.Round(cost.DuringLoanMonthly.Expected, MidpointRounding.AwayFromZero), lines.Sum(l => l.Low));
        }
    }

    [Fact]
    public void DuringLoanLines_ShippedScenarioSweep_NeverPrintsARangeBackwards()
    {
        Scenario scenario = Shipped();

        for (decimal price = 6000m; price <= 35000m; price += 1m)
        {
            IReadOnlyList<RoundedLine> lines = Format.DuringLoanLines(CostAt(scenario, price));

            Assert.All(lines, l => Assert.True(l.Low <= l.High, $"{l.Label} printed {l.Figure} at {price}"));
        }
    }

    // Loose inputs narrow enough that a range's whole band sits inside one dollar: rounding the low
    // and high ends independently printed "$80-$79" for a $6,036 vehicle.
    [Fact]
    public void DuringLoanLines_RangesNarrowerThanADollar_AddUpAndNeverPrintBackwards()
    {
        Scenario scenario = WithCents(95.50m, 50m, 3.21m) with
        {
            GasPricePerGallon = Parameter.Loose(3.20m, 3.21m),
            Apr = Parameter.Loose(0.08m, 0.0801m),
        };

        for (decimal price = 3000m; price <= 45000m; price += 1m)
        {
            CostBreakdown cost = CostAt(scenario, price);

            AssertBothSidesAddUp(cost, $"price {price}");
            Assert.All(Format.DuringLoanLines(cost), l => Assert.True(l.Low <= l.High, $"{l.Label} printed {l.Figure} at {price}"));
        }
    }

    // A ranged line whose band sits inside one dollar, next to a low side that needs a round-up the
    // high side does not: the largest low remainder is the fuel line, which would print "$60-$59".
    [Fact]
    public void RoundedToTotals_SubDollarFuelBandWhenOnlyTheLowSideRoundsUp_DoesNotPrintFuelBackwards()
    {
        Band[] parts = [Range(99.21m, 104.01m), Band.Point(115.06m), Range(59.23m, 59.42m), Band.Point(80m), Band.Point(60m)];

        AssertReconciled(parts, "$100-$104", "$59-$59");
    }

    // The fixed lines' remainders ask for two round-ups, which leaves the high side none for a
    // payment band that sits inside one dollar.
    [Fact]
    public void RoundedToTotals_SubDollarPaymentBandWhenFixedRemaindersWantTwoRoundUps_DoesNotPrintPaymentBackwards()
    {
        Band[] parts = [Range(30.5631m, 30.5748m), Band.Point(50.15m), Range(43.4615m, 51.1538m), Band.Point(52.7333m), Band.Point(46.70m)];

        AssertReconciled(parts, "$30-$30", "$44-$51");
    }

    private static Band Range(decimal low, decimal high) => new(low, (low + high) / 2m, high);

    private static void AssertReconciled(Band[] parts, params string[] rangedFigures)
    {
        var total = new Band(parts.Sum(p => p.Low), parts.Sum(p => p.Expected), parts.Sum(p => p.High));
        (decimal[] lows, decimal[] highs) = Format.RoundedToTotals(parts, total);

        Assert.Equal(Math.Round(total.Low, MidpointRounding.AwayFromZero), lows.Sum());
        Assert.Equal(Math.Round(total.High, MidpointRounding.AwayFromZero), highs.Sum());
        for (int i = 0; i < parts.Length; i++)
        {
            Assert.True(lows[i] <= highs[i], $"part {i} printed ${lows[i]}-${highs[i]}");
            if (!parts[i].IsRange)
            {
                Assert.Equal(lows[i], highs[i]);
            }
        }

        string[] figures = [.. Enumerable.Range(0, parts.Length).Where(i => parts[i].IsRange).Select(i => new RoundedLine("", lows[i], highs[i], true).Figure)];
        Assert.Equal(rangedFigures, figures);
    }

    [Fact]
    public void RankDetailAndShow_ShippedScenarioAt15333_PrintTheSameLoanPaymentAndTotal()
    {
        Score score = ScoreAt(Shipped(), 15333m);

        string rankPayment = Assert.Single(RenderRankDetail(score), line => line.Contains("loan payment $")).Trim();
        string[] show = RenderShow(score.Cost!);

        Assert.Equal("loan payment $269-$290  of during $537-$569", rankPayment);
        Assert.Equal(["Loan", "payment", "$269-$290"], Cells(show[1]));
        Assert.Equal(["During-loan", "total", "$537-$569"], Cells(show[6]));
    }

    [Fact]
    public void RankDetailAndShow_SweepOfPrices_PrintTheSameFiguresForEveryItemizedLine()
    {
        Scenario scenario = Shipped();

        for (decimal price = 6000m; price <= 35000m; price += 37m)
        {
            Score score = ScoreAt(scenario, price);
            IReadOnlyList<RoundedLine> lines = Format.DuringLoanLines(score.Cost!);

            string rankPayment = Assert.Single(RenderRankDetail(score), line => line.Contains("loan payment $")).Trim();
            string[] show = RenderShow(score.Cost!);

            Assert.Equal($"loan payment {lines[0].Figure}  of during {Format.Band(score.Cost!.DuringLoanMonthly)}", rankPayment);
            for (int i = 0; i < lines.Count; i++)
            {
                Assert.Equal($"{lines[i].Label} {lines[i].Figure}", string.Join(' ', Cells(show[i + 1])));
            }
        }
    }

    private static string[] Cells(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string[] RenderShow(CostBreakdown cost)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;
        ShowRenderer.RenderMonthlyCost(console, cost, null);
        return console.Output.Replace("\r\n", "\n").Split('\n');
    }

    private static string[] RenderRankDetail(Score score)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            var console = new TestConsole();
            console.Profile.Width = 80;
            console.Profile.Capabilities.Ansi = false;
            RankRenderer.Render(console, [score], null, new Dictionary<string, ResearchStatus>(), [], detail: true);
            return console.Output.Replace("\r\n", "\n").Split('\n');
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }
}
