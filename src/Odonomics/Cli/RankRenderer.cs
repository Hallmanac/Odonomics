using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli;

public static class RankRenderer
{
    public static void Render(IReadOnlyList<Score> scores, decimal? budget, IReadOnlyDictionary<string, ResearchStatus> research)
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

        RenderRanked($"Ranked ({ranked.Count})", ranked, research);

        if (budget is decimal budgetValue && overBudget.Count > 0)
        {
            overBudget.Sort(ByTenYearAverage);
            RenderRanked($"Over the ${budgetValue:N0} budget ({overBudget.Count})", overBudget, research);
        }

        RenderInsuranceUnknown(insuranceUnknown);
        RenderExcluded(excluded);
        RenderFGradedOnly(scores);
    }

    private static void RenderRanked(string heading, IReadOnlyList<Score> scores, IReadOnlyDictionary<string, ResearchStatus> research)
    {
        AnsiConsole.MarkupLine($"[bold]{heading}[/]");
        if (scores.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("VIN");
        table.AddColumn("Vehicle");
        table.AddColumn("Price");
        table.AddColumn("During-loan");
        table.AddColumn("10yr avg");
        table.AddColumn("Research");
        table.AddColumn("Grade");

        foreach (Score score in scores)
        {
            CostBreakdown cost = score.Cost!;
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"),
                Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m),
                Format.Band(cost.DuringLoanMonthly),
                Format.Band(cost.TenYearAverageMonthly),
                ResearchCell(research.GetValueOrDefault(score.Vehicle.Vin)),
                Format.Cell(score.Vehicle.DealerGrade ?? "-"));
        }

        AnsiConsole.Write(table);
    }

    private static string ResearchCell(ResearchStatus? status) => status switch
    {
        null or { Researched: false } => Format.Cell("not researched"),
        { HasRedFlag: true } => "[red]red flag[/]",
        _ => "[green]clean[/]",
    };

    private static void RenderInsuranceUnknown(IReadOnlyList<Score> scores)
    {
        AnsiConsole.MarkupLine($"[bold yellow]Not ranked: insurance unknown ({scores.Count})[/]");
        if (scores.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        AnsiConsole.MarkupLine("[yellow]No insurance figure for this model in the scenario; add one to insuranceMonthlyByModel to rank it.[/]");
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("VIN");
        table.AddColumn("Vehicle");
        table.AddColumn("Price");
        table.AddColumn("Grade");

        foreach (Score score in scores)
        {
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"),
                Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m),
                Format.Cell(score.Vehicle.DealerGrade ?? "-"));
        }

        AnsiConsole.Write(table);
    }

    /// <summary>A vehicle whose every posting comes from an F-graded dealer, surfaced under its
    /// own heading regardless of which other section (if any) it also appears in above: this is a
    /// warning lens on top of the ranking, not another filter, so nothing here is ever hidden.</summary>
    private static void RenderFGradedOnly(IReadOnlyList<Score> scores)
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

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold red]F-graded dealer only, verify before contact ({flagged.Count})[/]");
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("VIN");
        table.AddColumn("Vehicle");
        table.AddColumn("Price");
        table.AddColumn("Grade");

        foreach (Score score in flagged)
        {
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"),
                Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m),
                Format.Cell(score.Vehicle.DealerGrade ?? "F"));
        }

        AnsiConsole.Write(table);
    }

    private static void RenderExcluded(IReadOnlyList<Score> scores)
    {
        AnsiConsole.MarkupLine($"[bold]Excluded ({scores.Count})[/]");
        if (scores.Count == 0)
        {
            AnsiConsole.MarkupLine("  none");
            return;
        }

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("VIN");
        table.AddColumn("Vehicle");
        table.AddColumn("Reasons");

        foreach (Score score in scores)
        {
            table.AddRow(Format.Cell(score.Vehicle.Vin), Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"), Format.Cell(string.Join("; ", score.FailureReasons)));
        }

        AnsiConsole.Write(table);
    }
}
