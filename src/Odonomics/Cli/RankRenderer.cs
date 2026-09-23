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
    public static void Render(IAnsiConsole console, IReadOnlyList<Score> scores, decimal? budget, IReadOnlyDictionary<string, ResearchStatus> research)
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

        RenderRanked(console, $"Ranked ({ranked.Count})", ranked, research);

        if (budget is decimal budgetValue && overBudget.Count > 0)
        {
            overBudget.Sort(ByTenYearAverage);
            RenderRanked(console, $"Over the ${budgetValue:N0} budget ({overBudget.Count})", overBudget, research);
        }

        RenderInsuranceUnknown(console, insuranceUnknown);
        RenderExcluded(console, excluded);
        RenderFGradedOnly(console, scores);
    }

    private const int VehicleNameMaxWidth = 40;

    private static void RenderRanked(IAnsiConsole console, string heading, IReadOnlyList<Score> scores, IReadOnlyDictionary<string, ResearchStatus> research)
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

        foreach (Score score in scores)
        {
            ResearchStatus? status = research.GetValueOrDefault(score.Vehicle.Vin);
            console.MarkupLine($"  {Format.Cell(score.Vehicle.Vin)}  {Format.Cell(VehicleName(score))}");
            console.MarkupLine(RankedDetailLine(score, status, anyGraded));
        }
    }

    private static string VehicleName(Score score) =>
        Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", VehicleNameMaxWidth);

    private static string RankedDetailLine(Score score, ResearchStatus? status, bool anyGraded)
    {
        CostBreakdown cost = score.Cost!;
        string line = $"  {Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m)}  during {Format.Band(cost.DuringLoanMonthly)}  10yr avg {Format.Band(cost.TenYearAverageMonthly)}  {ResearchMarker(status)}  rc {RecallsText(status)}";
        return anyGraded ? $"{line}  gr {Format.Cell(score.Vehicle.DealerGrade ?? "-")}" : line;
    }

    private static string ResearchMarker(ResearchStatus? status) => status switch
    {
        null or { Researched: false } => "-",
        { HasRedFlag: true } => "[red]flag[/]",
        _ => "[green]clean[/]",
    };

    private static string RecallsText(ResearchStatus? status) =>
        status is { Researched: true, RecallsKnown: true } s ? s.RecallCount.ToString() : "-";

    private const int VinColumnWidth = 17;
    private const int VehicleColumnWidth = 24;
    private const int PriceColumnWidth = 9;
    private const int GradeColumnWidth = 6;

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
        table.AddColumn(new TableColumn("Vehicle") { Width = VehicleColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Price") { Width = PriceColumnWidth, NoWrap = true });
        if (includeGrade)
        {
            table.AddColumn(new TableColumn("Grade") { Width = GradeColumnWidth, NoWrap = true });
        }

        return table;
    }

    private static void AddVehiclePriceRow(Table table, Score score, bool includeGrade, string defaultGrade)
    {
        string vehicle = Format.Cell(Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", VehicleColumnWidth));
        string price = Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m);
        if (includeGrade)
        {
            table.AddRow(Format.Cell(score.Vehicle.Vin), vehicle, price, Format.Cell(score.Vehicle.DealerGrade ?? defaultGrade));
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
        table.AddColumn(new TableColumn("Vehicle") { Width = VehicleColumnWidth, NoWrap = true });
        table.AddColumn("Reasons");

        foreach (Score score in scores)
        {
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell(Format.Truncate($"{score.Vehicle.Year} {score.Vehicle.MakeModel}", VehicleColumnWidth)),
                Format.Cell(string.Join("; ", score.FailureReasons)));
        }

        console.Write(table);
    }
}
