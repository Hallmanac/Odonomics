using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli;

public static class RankRenderer
{
    public static void Render(IReadOnlyList<Score> scores, decimal? budget)
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

        RenderRanked($"Ranked ({ranked.Count})", ranked);

        if (budget is decimal budgetValue && overBudget.Count > 0)
        {
            overBudget.Sort(ByTenYearAverage);
            RenderRanked($"Over the ${budgetValue:N0} budget ({overBudget.Count})", overBudget);
        }

        RenderInsuranceUnknown(insuranceUnknown);
        RenderExcluded(excluded);
    }

    private static void RenderRanked(string heading, IReadOnlyList<Score> scores)
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

        foreach (Score score in scores)
        {
            CostBreakdown cost = score.Cost!;
            table.AddRow(
                Format.Cell(score.Vehicle.Vin),
                Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"),
                Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m),
                Format.Band(cost.DuringLoanMonthly),
                Format.Band(cost.TenYearAverageMonthly));
        }

        AnsiConsole.Write(table);
    }

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

        foreach (Score score in scores)
        {
            table.AddRow(Format.Cell(score.Vehicle.Vin), Format.Cell($"{score.Vehicle.Year} {score.Vehicle.MakeModel}"), Format.Money(score.Vehicle.LowestCurrentPrice ?? 0m));
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
