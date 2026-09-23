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
        decimal[] itemized = RoundedToTotal([running.Insurance, running.Fuel, running.Maintenance, running.Reserve], running.Total);
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
    }

    /// <summary>Rounds each part to whole dollars so the parts add up to the total's own rounding,
    /// which independent rounding does not guarantee: the whole dollars the total is short of the
    /// parts' floors go to the parts with the largest fractional remainders.</summary>
    private static decimal[] RoundedToTotal(decimal[] parts, decimal total)
    {
        decimal[] floors = [.. parts.Select(Math.Floor)];
        int shortfall = (int)(Math.Round(total, MidpointRounding.AwayFromZero) - floors.Sum());
        HashSet<int> roundedUp = [.. Enumerable.Range(0, parts.Length)
            .OrderByDescending(i => parts[i] - floors[i])
            .Take(Math.Max(shortfall, 0))];

        return [.. floors.Select((floor, i) => roundedUp.Contains(i) ? floor + 1m : floor)];
    }
}
