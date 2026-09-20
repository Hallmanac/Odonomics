using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class BudgetCommand
{
    public static Task<int> RunAsync(string scenarioPath, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        decimal insuranceMonthly = scenario.AverageKnownInsuranceMonthly();
        decimal mpg = scenario.AverageMpg();

        AnsiConsole.MarkupLine(
            "[bold]Max purchase price by target monthly budget[/] (using the scenario's average known insurance and mpg across target models)");

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Monthly budget");
        table.AddColumn("Max purchase price");

        foreach (decimal budget in scenario.TargetMonthlyBudgets)
        {
            decimal maxPrice = BudgetSolver.MaxPurchasePrice(scenario, budget, insuranceMonthly, mpg);
            table.AddRow(Format.Money(budget), Format.Money(maxPrice));
        }

        AnsiConsole.Write(table);
        return Task.FromResult(0);
    }
}
