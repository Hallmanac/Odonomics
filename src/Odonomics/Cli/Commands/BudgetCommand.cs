using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class BudgetCommand
{
    public static Task<int> RunAsync(string scenarioPath, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        Render(AnsiConsole.Console, scenario);
        return Task.FromResult(0);
    }

    /// <summary>Prints the running-cost block and the max-price table. The block comes first so a
    /// reader sees how much of each target is already spoken for before the price it leaves. Both
    /// take their figures from <see cref="BudgetSolver"/>, so neither can drift from the solver.</summary>
    public static void Render(IAnsiConsole console, Scenario scenario)
    {
        decimal insuranceMonthly = scenario.AverageKnownInsuranceMonthly();
        decimal mpg = scenario.AverageMpg();
        MonthlyRunningCosts running = BudgetSolver.RunningCosts(scenario, insuranceMonthly, mpg);

        console.MarkupLine(
            "[bold]Max purchase price by target monthly budget[/] (using the scenario's average known insurance and mpg across target models)");
        console.MarkupLine(
            $"Running costs before any payment: about {Format.Money(running.Total)} a month (insurance {Format.Money(running.Insurance)}, fuel {Format.Money(running.Fuel)}, maintenance {Format.Money(running.Maintenance)}, reserve {Format.Money(running.Reserve)})");

        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn("Monthly budget");
        table.AddColumn("Payment room");
        table.AddColumn("Max purchase price");

        foreach (decimal budget in scenario.TargetMonthlyBudgets)
        {
            decimal maxPrice = BudgetSolver.MaxPurchasePrice(scenario, budget, insuranceMonthly, mpg);
            decimal paymentRoom = Math.Max(0m, budget - running.Total);
            table.AddRow(Format.Money(budget), Format.Money(paymentRoom), Format.Money(maxPrice));
        }

        console.Write(table);
    }
}
