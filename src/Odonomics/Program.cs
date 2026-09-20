using System.CommandLine;
using Odonomics.Cli.Commands;
using Spectre.Console;

const string defaultScenarioPath = "scenarios/daughter.json";

var scenarioOption = new Option<string>("--scenario") { Description = "path to the scenario JSON file", DefaultValueFactory = _ => defaultScenarioPath };

var rootCommand = new RootCommand("odo - a used-car search and total-cost-of-ownership CLI for one purchase");

var searchCommand = new Command("search", "run Auto.dev and Marketcheck, upsert the ledger, print what's new, price-dropped, and gone");
searchCommand.Add(scenarioOption);
searchCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    return await SearchCommand.RunAsync(scenarioPath, cancellationToken);
});
rootCommand.Add(searchCommand);

var walkSiteArgument = new Argument<string>("site") { Description = "cars.com or carvana" };
var walkModelOption = new Option<string?>("--model") { Description = "which target model to visit this run (\"Make Model\"); defaults to the scenario's first allowed model" };
var walkMaxOption = new Option<int>("--max") { Description = "maximum number of detail pages to visit", DefaultValueFactory = _ => Odonomics.Walk.WalkPacing.DefaultMaxDetailPages };
var walkCommand = new Command("walk", "an operator-assisted walk of one site, connected over CDP to a browser you already launched");
walkCommand.Add(walkSiteArgument);
walkCommand.Add(scenarioOption);
walkCommand.Add(walkModelOption);
walkCommand.Add(walkMaxOption);
walkCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string site = parseResult.GetValue(walkSiteArgument)!;
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    string? model = parseResult.GetValue(walkModelOption);
    int max = parseResult.GetValue(walkMaxOption);
    return await WalkCommand.RunAsync(scenarioPath, site, model, max, cancellationToken);
});
rootCommand.Add(walkCommand);

var rankBudgetOption = new Option<decimal?>("--budget") { Description = "hide vehicles whose during-loan monthly cost exceeds this" };
var rankTermOption = new Option<int?>("--term") { Description = "override the scenario's loan term in months (e.g. 48, 60, 72)" };
var rankCommand = new Command("rank", "score every vehicle in the ledger against the scenario");
rankCommand.Add(scenarioOption);
rankCommand.Add(rankBudgetOption);
rankCommand.Add(rankTermOption);
rankCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    decimal? budget = parseResult.GetValue(rankBudgetOption);
    int? term = parseResult.GetValue(rankTermOption);
    return await RankCommand.RunAsync(scenarioPath, budget, term, cancellationToken);
});
rootCommand.Add(rankCommand);

var showVinArgument = new Argument<string>("vin");
var showCommand = new Command("show", "NHTSA decode, recalls, complaints, postings, notes, and finalist status for one VIN");
showCommand.Add(showVinArgument);
showCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string vin = parseResult.GetValue(showVinArgument)!;
    return await ShowCommand.RunAsync(vin, cancellationToken);
});
rootCommand.Add(showCommand);

var noteVinArgument = new Argument<string>("vin");
var noteTextArgument = new Argument<string>("text");
var noteCommand = new Command("note", "attach a free-text note to a vehicle");
noteCommand.Add(noteVinArgument);
noteCommand.Add(noteTextArgument);
noteCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string vin = parseResult.GetValue(noteVinArgument)!;
    string text = parseResult.GetValue(noteTextArgument)!;
    return await NoteCommand.RunAsync(vin, text, cancellationToken);
});
rootCommand.Add(noteCommand);

var finalistVinArgument = new Argument<string>("vin");
var finalistCommand = new Command("finalist", "mark a vehicle a finalist (needs a PPI note and a Carfax or AutoCheck note)");
finalistCommand.Add(finalistVinArgument);
finalistCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string vin = parseResult.GetValue(finalistVinArgument)!;
    return await FinalistCommand.RunAsync(vin, cancellationToken);
});
rootCommand.Add(finalistCommand);

var budgetCommand = new Command("budget", "the max purchase price under the scenario for each target monthly budget");
budgetCommand.Add(scenarioOption);
budgetCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    return await BudgetCommand.RunAsync(scenarioPath, cancellationToken);
});
rootCommand.Add(budgetCommand);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    return await rootCommand.Parse(args).InvokeAsync(cancellationToken: cts.Token);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLineInterpolated($"[red]error: {ex.Message}[/]");
    return 1;
}
