using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Prints `odo rank`'s sections: ranked, over-budget, insurance-unknown, excluded, and
/// F-graded-only. Takes an explicit <see cref="IAnsiConsole"/> (rather than writing through the
/// static <c>AnsiConsole</c>) so a rendering test can capture the output the same way
/// <c>ResearchSummaryRenderer</c>'s tests do. The ranked and over-budget sections print one or two
/// plain lines per vehicle rather than a table: an eight-field table can't fit at 80 columns
/// without wrapping a VIN or a dollar range mid-value, which is exactly the defect this shape
/// replaces.</summary>
public static class RankRenderer
{
    public static void Render(
        IAnsiConsole console,
        IReadOnlyList<Score> scores,
        decimal? budget,
        IReadOnlyDictionary<string, ResearchStatus> research,
        IReadOnlyList<decimal> targetMonthlyBudgets,
        bool detail = false)
    {
        List<Score> ranked = [];
        List<Score> overBudget = [];
        List<Score> insuranceUnknown = [];
        List<Score> excluded = [];

        foreach (Score score in scores)
        {
            if (!score.Passes)
            {
                excluded.Add(score);
                continue;
            }

            if (score.InsuranceUnknown)
            {
                insuranceUnknown.Add(score);
                continue;
            }

            if (score.Cost is null)
            {
                continue; // no current price; already covered by a filter reason, nothing more to show
            }

            if (budget is decimal b && score.Cost.DuringLoanMonthly.Expected > b)
            {
                overBudget.Add(score);
                continue;
            }

            ranked.Add(score);
        }

        int ByTenYearAverage(Score a, Score b) => a.Cost!.TenYearAverageMonthly.Expected.CompareTo(b.Cost!.TenYearAverageMonthly.Expected);
        ranked.Sort(ByTenYearAverage);

        RenderRunningCostsNote(console, [.. ranked, .. overBudget], detail);
        RenderUnmetTargets(console, [.. ranked, .. overBudget], targetMonthlyBudgets);
        RenderRanked(console, $"Ranked ({ranked.Count})", ranked, research, detail);

        if (budget is decimal budgetValue && overBudget.Count > 0)
        {
            overBudget.Sort(ByTenYearAverage);
            RenderRanked(console, $"Over the ${budgetValue:N0} budget ({overBudget.Count})", overBudget, research, detail);
        }

        RenderInsuranceUnknown(console, insuranceUnknown);
        RenderExcluded(console, excluded);
        RenderFGradedOnly(console, scores);
    }

    /// <summary>Says plainly, above the Ranked section, which of the scenario's target monthly
    /// budgets no rankable vehicle meets during the loan, and points at <c>odo budget</c> for the
    /// purchase price each target allows. A rankable vehicle is one that passes the filters, has a
    /// known insurance figure, and has a cost. Deliberately driven by the scenario's own targets
    /// and not by rank's optional --budget flag, so it takes every rankable vehicle, including
    /// those the flag moved into the over-budget section: the cheapest band it names may belong to
    /// a vehicle listed under the over-budget heading rather than Ranked. Prints nothing when
    /// there is no rankable vehicle to name or when the cheapest one meets every target. It is
    /// written as markup for its yellow style; a "$592-$627" band has no space in it, so
    /// Spectre's word wrapping never splits a dollar figure.</summary>
    private static void RenderUnmetTargets(IAnsiConsole console, IReadOnlyList<Score> rankable, IReadOnlyList<decimal> targetMonthlyBudgets)
    {
        if (rankable.Count == 0)
        {
            return;
        }

        Band cheapest = rankable.Select(s => s.Cost!.DuringLoanMonthly).OrderBy(band => band.Expected).First();
        List<decimal> unmet = [.. targetMonthlyBudgets
            .Where(target => cheapest.Expected > target)
            .Distinct()
            .Order()];
        if (unmet.Count == 0)
        {
            return;
        }

        console.MarkupLine($"[yellow]No rankable vehicle meets a target budget of {JoinTargets(unmet)} during the loan; the cheapest is {Format.Band(cheapest)} a month. Run odo budget for the purchase price each target allows.[/]");
    }

    /// <summary>Says above the Ranked section what During-loan and 10yr avg include besides the loan
    /// payment, so a "$592-$627" during-loan figure is not read as a car payment, and, unless
    /// <paramref name="detail"/> already prints them, points at <c>--detail</c> for each vehicle's
    /// own payment. Only During-loan is the payment plus the running costs; 10yr avg also spreads the
    /// down payment, the payments made within the hold, and the resale value over the hold, so the
    /// note does not offer the remainder as the payment for both columns. The running costs are the
    /// scenario's own insurance, fuel, maintenance, and reserve as the cost model priced them for the
    /// listed vehicles: one figure or range when they agree, otherwise the span from the lowest low
    /// to the highest high. Covers the same rankable vehicles as <see cref="RenderUnmetTargets"/>,
    /// and prints nothing when there are none.</summary>
    private static void RenderRunningCostsNote(IAnsiConsole console, IReadOnlyList<Score> rankable, bool detail)
    {
        if (rankable.Count == 0)
        {
            return;
        }

        var running = new Band(
            rankable.Min(s => s.Cost!.AfterPayoffMonthly.Low),
            rankable.Min(s => s.Cost!.AfterPayoffMonthly.Expected),
            rankable.Max(s => s.Cost!.AfterPayoffMonthly.High));

        string pointer = detail
            ? string.Empty
            : " Run with --detail to see each vehicle's payment.";
        console.MarkupLine($"During-loan is the loan payment plus running costs of about {Format.Band(running)} a month (insurance, fuel, maintenance, reserve). 10yr avg spreads those costs, the down payment, the loan payments, and resale value over the hold.{pointer}");
    }

    private static string JoinTargets(IReadOnlyList<decimal> targets) => targets switch
    {
        [decimal only] => Format.Money(only),
        [decimal first, decimal second] => $"{Format.Money(first)} or {Format.Money(second)}",
        _ => $"{string.Join(", ", targets.Take(targets.Count - 1).Select(Format.Money))}, or {Format.Money(targets[^1])}",
    };

    private const int VehicleNameMaxWidth = 40;

    private static void RenderRanked(IAnsiConsole console, string heading, IReadOnlyList<Score> scores, IReadOnlyDictionary<string, ResearchStatus> research, bool detail)
    {
        console.MarkupLine($"[bold]{heading}[/]");
        if (scores.Count == 0)
        {
            console.MarkupLine("  none");
            return;
        }

        bool anyGraded = scores.Any(s => s.Vehicle.DealerGrade is not null);
        if (!anyGraded)
        {
            console.MarkupLine("  Grade: no vehicle here has a CarEdge grade yet.");
        }

        console.MarkupLine("  Research: clean, flag, or - for not yet researched.");
        console.MarkupLine(anyGraded
            ? "  rc: open recall count. gr: CarEdge dealer grade(s)."
            : "  rc: open recall count.");

        foreach (Score score in scores)
        {
            ResearchStatus? status = research.GetValueOrDefault(score.Vehicle.Vin);
            console.MarkupLine($"  {Format.Cell(score.Vehicle.Vin)}  {Format.Cell(VehicleName(score))}");
            console.MarkupLine(RankedDetailLine(score, status, anyGraded));
            if (detail)
            {
                console.MarkupLine(PaymentLine(score.Cost!));
            }
        }
    }

    private static string VehicleName(Score score) =>
        Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", VehicleNameMaxWidth);

    private static string RankedDetailLine(Score score, ResearchStatus? status, bool anyGraded)
    {
        CostBreakdown cost = score.Cost!;
        string line = $"  {PriceText(score)}  during {Format.Band(cost.DuringLoanMonthly)}  10yr avg {Format.Band(cost.TenYearAverageMonthly)}  {ResearchMarker(status)}  rc {RecallsText(status)}";
        return anyGraded
            ? $"{line}  gr {Format.Cell(Format.Truncate(score.Vehicle.DealerGrade ?? "-", GradeColumnWidth))}"
            : line;
    }

    /// <summary>The `--detail` line under a row: the loan payment on its own beside the during-loan
    /// total it is part of. The payment is the same rounded figure `odo show` itemizes (see
    /// <see cref="Format.DuringLoanLines"/>), so the two commands agree on it. Its own line, not a
    /// column, so the row above keeps its 80-column fit.</summary>
    private static string PaymentLine(CostBreakdown cost) =>
        $"    loan payment {Format.DuringLoanLines(cost)[0].Figure}  of during {Format.Band(cost.DuringLoanMonthly)}";

    /// <summary>A vehicle with no current asking price shows "-" rather than "$0", which would read
    /// as a real price of zero.</summary>
    private static string PriceText(Score score) =>
        score.Vehicle.LowestCurrentPrice is decimal price
            ? Format.Money(price)
            : "-";

    private static string ResearchMarker(ResearchStatus? status) => status switch
    {
        null or { Researched: false } => "-",
        { HasRedFlag: true } => "[red]flag[/]",
        _ => "[green]clean[/]",
    };

    private static string RecallsText(ResearchStatus? status) =>
        status is { Researched: true, RecallsKnown: true } s
            ? s.RecallCount.ToString()
            : "-";

    private const int VinColumnWidth = 17;
    private const int PriceColumnWidth = 9;
    private const int GradeColumnWidth = 6;
    private const int ExcludedVehicleColumnWidth = 24;

    /// <summary>The Vehicle column takes whatever's left of 80 after the fixed columns and
    /// Border.Minimal's own per-column padding and separators (3 chars per column plus 1 for the
    /// table's own edges, the same math <c>WalkCommand</c>'s summary table uses): unlike the
    /// Excluded table, neither the insurance-unknown nor the F-graded-only table has a flexible
    /// column competing for width, so leaving Vehicle at a narrower fixed width than that would
    /// truncate names for no reason.</summary>
    private static int VehicleColumnWidth(bool includeGrade)
    {
        int columnCount = includeGrade ? 4 : 3;
        int overhead = (3 * columnCount) + 1;
        int fixedWidth = VinColumnWidth + PriceColumnWidth + (includeGrade ? GradeColumnWidth : 0);
        return 80 - fixedWidth - overhead;
    }

    private static void RenderInsuranceUnknown(IAnsiConsole console, IReadOnlyList<Score> scores)
    {
        console.MarkupLine($"[bold yellow]Not ranked: insurance unknown ({scores.Count})[/]");
        if (scores.Count == 0)
        {
            console.MarkupLine("  none");
            return;
        }

        console.MarkupLine("[yellow]No insurance figure for this model in the scenario; add one to insuranceMonthlyByModel to rank it.[/]");

        bool anyGraded = scores.Any(s => s.Vehicle.DealerGrade is not null);
        if (!anyGraded)
        {
            console.MarkupLine("  Grade: no vehicle here has a CarEdge grade yet.");
        }

        Table table = VehiclePriceTable(anyGraded);
        foreach (Score score in scores)
        {
            AddVehiclePriceRow(table, score, anyGraded, defaultGrade: "-");
        }

        console.Write(table);
    }

    /// <summary>A vehicle whose every posting comes from an F-graded dealer, surfaced under its
    /// own heading regardless of which other section (if any) it also appears in above: this is a
    /// warning lens on top of the ranking, not another filter, so nothing here is ever hidden.</summary>
    private static void RenderFGradedOnly(IAnsiConsole console, IReadOnlyList<Score> scores)
    {
        List<Score> flagged = [.. scores.Where(s => s.Vehicle.OnlyFGradedDealers)];
        if (flagged.Count == 0)
        {
            return;
        }

        flagged.Sort((a, b) => (a.Cost, b.Cost) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => a.Cost!.TenYearAverageMonthly.Expected.CompareTo(b.Cost!.TenYearAverageMonthly.Expected),
        });

        console.WriteLine();
        console.MarkupLine($"[bold red]F-graded dealer only, verify before contact ({flagged.Count})[/]");

        Table table = VehiclePriceTable(includeGrade: true);
        foreach (Score score in flagged)
        {
            AddVehiclePriceRow(table, score, includeGrade: true, defaultGrade: "F");
        }

        console.Write(table);
    }

    private static Table VehiclePriceTable(bool includeGrade)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("VIN") { Width = VinColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Vehicle") { Width = VehicleColumnWidth(includeGrade), NoWrap = true });
        table.AddColumn(new TableColumn("Price") { Width = PriceColumnWidth, NoWrap = true });
        if (includeGrade)
        {
            table.AddColumn(new TableColumn("Grade") { Width = GradeColumnWidth, NoWrap = true });
        }

        return table;
    }

    private static void AddVehiclePriceRow(Table table, Score score, bool includeGrade, string defaultGrade)
    {
        string vehicle = Format.Cell(Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", VehicleColumnWidth(includeGrade)));
        string price = Format.Cell(Format.Truncate(PriceText(score), PriceColumnWidth));
        if (includeGrade)
        {
            string grade = Format.Truncate(score.Vehicle.DealerGrade ?? defaultGrade, GradeColumnWidth);
            table.AddRow(Format.Cell(score.Vehicle.Vin), vehicle, price, Format.Cell(grade));
        }
        else
        {
            table.AddRow(Format.Cell(score.Vehicle.Vin), vehicle, price);
        }
    }

    private static void RenderExcluded(IAnsiConsole console, IReadOnlyList<Score> scores)
    {
        console.MarkupLine($"[bold]Excluded ({scores.Count})[/]");
        if (scores.Count == 0)
        {
            console.MarkupLine("  none");
            return;
        }

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("VIN") { Width = VinColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Vehicle") { Width = ExcludedVehicleColumnWidth, NoWrap = true });
        table.AddColumn("Reasons");

        foreach (Score score in scores)
        {
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell(Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", ExcludedVehicleColumnWidth)),
                Format.Cell(string.Join("; ", score.FailureReasons)));
        }

        console.Write(table);
    }
}
