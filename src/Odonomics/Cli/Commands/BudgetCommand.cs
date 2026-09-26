using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class BudgetCommand
{
    public static Task<int> RunAsync(string scenarioPath, Fulfillment? fulfillment, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        if (fulfillment is Fulfillment overrideFulfillment)
        {
            scenario = scenario with { Fulfillment = overrideFulfillment };
        }

        Render(AnsiConsole.Console, scenario);
        return Task.FromResult(0);
    }

    /// <summary>Prints the running-cost block and the max-price table. The block comes first so a
    /// reader sees how much of each target is already spoken for before the price it leaves. Both
    /// take their figures from <see cref="BudgetSolver"/>, so neither can drift from the solver. A
    /// closing line says the max price counts a listing's fee, the shipping fee under delivery or the
    /// pickup fee under pickup, and which of the two this run assumed, since `odo rank` prices a
    /// vehicle at its asking price plus that fee and the two commands must agree on what a price is.</summary>
    public static void Render(IAnsiConsole console, Scenario scenario)
    {
        decimal insuranceMonthly = scenario.AverageKnownInsuranceMonthly();
        decimal mpg = scenario.AverageMpg();
        MonthlyRunningCosts running = BudgetSolver.RunningCosts(scenario, insuranceMonthly, mpg);

        console.MarkupLine(
            "[bold]Max purchase price by target monthly budget[/] (using the scenario's average known insurance and mpg across target models)");
        decimal[] itemized = Format.RoundedToTotal([running.Insurance, running.Fuel, running.Maintenance, running.Reserve], running.Total);
        console.MarkupLine(
            $"Running costs before any payment: about {Format.Money(running.Total)} a month (insurance {Format.Money(itemized[0])}, fuel {Format.Money(itemized[1])}, maintenance {Format.Money(itemized[2])}, reserve {Format.Money(itemized[3])})");

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
        console.MarkupLine(scenario.Fulfillment switch
        {
            Fulfillment.Pickup => "Max purchase price is the asking price plus any pickup fee (Carvana's hub pickup prints none), assuming pickup, so a car with a pickup fee needs an asking price that much lower.",
            _ => "Max purchase price is the asking price plus any shipping fee (Carvana lists one per car), assuming delivery, so a car with a shipping fee needs an asking price that much lower.",
        });
    }
}
