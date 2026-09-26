using System.CommandLine;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
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

var walkSiteArgument = new Argument<string?>("site") { Description = "cars.com, carvana, autotrader, or carmax; omit to walk all four", Arity = ArgumentArity.ZeroOrOne };
var walkModelOption = new Option<string?>("--model") { Description = "which target model to visit this run (\"Make Model\"); defaults to every model in the scenario's allowed list" };
var walkMaxOption = WalkCommand.CreateMaxOption();
var walkRevisitOption = WalkCommand.CreateRevisitOption();
var walkCommand = new Command("walk", "an operator-assisted walk, connected over CDP to a browser you already launched, of every site and model in the scenario (or a narrower slice via the site argument and --model)");
walkCommand.Add(walkSiteArgument);
walkCommand.Add(scenarioOption);
walkCommand.Add(walkModelOption);
walkCommand.Add(walkMaxOption);
walkCommand.Add(walkRevisitOption);
walkCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string? site = parseResult.GetValue(walkSiteArgument);
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    string? model = parseResult.GetValue(walkModelOption);
    int? max = parseResult.GetValue(walkMaxOption);
    bool revisit = parseResult.GetValue(walkRevisitOption);
    return await WalkCommand.RunAsync(scenarioPath, site, model, max, revisit, cancellationToken);
});
rootCommand.Add(walkCommand);

var fulfillmentOption = new Option<Fulfillment?>("--fulfillment") { Description = "override the scenario's fulfillment for this run: delivery counts a listing's shipping fee, pickup counts its pickup fee" };

var rankBudgetOption = new Option<decimal?>("--budget") { Description = "list vehicles whose during-loan monthly cost exceeds this under a separate over-budget heading instead of the ranked list" };
var rankTermOption = new Option<int?>("--term") { Description = "override the scenario's loan term in months (e.g. 48, 60, 72)" };
var rankDetailOption = new Option<bool>("--detail") { Description = "under each row, also print that vehicle's loan payment beside its during-loan total" };
var rankCommand = new Command("rank", "score every vehicle in the ledger against the scenario");
rankCommand.Add(scenarioOption);
rankCommand.Add(rankBudgetOption);
rankCommand.Add(rankTermOption);
rankCommand.Add(rankDetailOption);
rankCommand.Add(fulfillmentOption);
rankCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    decimal? budget = parseResult.GetValue(rankBudgetOption);
    int? term = parseResult.GetValue(rankTermOption);
    bool detail = parseResult.GetValue(rankDetailOption);
    Fulfillment? fulfillment = parseResult.GetValue(fulfillmentOption);
    return await RankCommand.RunAsync(scenarioPath, budget, term, detail, fulfillment, cancellationToken);
});
rootCommand.Add(rankCommand);

var refreshOption = new Option<bool>("--refresh") { Description = "re-fetch NHTSA safety ratings and Marketcheck VIN history even if the cached research is under seven days old" };

var showVinArgument = new Argument<string>("vin");
var showAllHistoryOption = new Option<bool>("--all-history") { Description = "also print the raw, one-row-per-sighting VIN history table underneath the grouped-by-seller summary" };
var showCommand = new Command("show", "NHTSA decode, recalls, complaints, safety ratings, Marketcheck VIN history, red flags, postings, notes, and finalist status for one VIN");
showCommand.Add(showVinArgument);
showCommand.Add(scenarioOption);
showCommand.Add(refreshOption);
showCommand.Add(showAllHistoryOption);
showCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string vin = parseResult.GetValue(showVinArgument)!;
    bool refresh = parseResult.GetValue(refreshOption);
    bool allHistory = parseResult.GetValue(showAllHistoryOption);
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    return await ShowCommand.RunAsync(vin, scenarioPath, refresh, allHistory, cancellationToken);
});
rootCommand.Add(showCommand);

var researchVinsArgument = new Argument<string[]>("vins") { Description = "specific VINs to research; omit to research every vehicle in the ledger that passes the scenario's filters", Arity = ArgumentArity.ZeroOrMore };
var researchQuietOption = new Option<bool>("--quiet") { Description = "replace the per-vehicle progress lines with a single counter line that overwrites itself" };
var researchCommand = new Command("research", "NHTSA safety ratings and Marketcheck VIN history for one or more vehicles in one go, with a red-flags summary at the end");
researchCommand.Add(researchVinsArgument);
researchCommand.Add(scenarioOption);
researchCommand.Add(refreshOption);
researchCommand.Add(researchQuietOption);
researchCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string[] vins = parseResult.GetValue(researchVinsArgument) ?? [];
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    bool refresh = parseResult.GetValue(refreshOption);
    bool quiet = parseResult.GetValue(researchQuietOption);
    return await ResearchCommand.RunAsync(scenarioPath, vins, refresh, quiet, cancellationToken);
});
rootCommand.Add(researchCommand);

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

var dealerGradeAllOption = new Option<bool>("--all") { Description = "grade every ungraded dealer in the ledger" };
var dealerGradeRefreshOption = new Option<bool>("--refresh") { Description = "also look up dealers that already have a grade again, to pick up their doc fee and add-ons note" };
var dealerGradeVinArgument = new Argument<string?>("vin") { Description = "grade only this VIN's dealer", Arity = ArgumentArity.ZeroOrOne };
var dealerGradeCommand = new Command("grade", "look up each ungraded dealer's CarEdge grade over the browser you already launched");
dealerGradeCommand.Add(dealerGradeAllOption);
dealerGradeCommand.Add(dealerGradeRefreshOption);
dealerGradeCommand.Add(dealerGradeVinArgument);
dealerGradeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    bool all = parseResult.GetValue(dealerGradeAllOption);
    string? vin = parseResult.GetValue(dealerGradeVinArgument);
    bool refresh = parseResult.GetValue(dealerGradeRefreshOption);
    return await DealerGradeCommand.RunAsync(all, vin, refresh, cancellationToken);
});
var dealerCommand = new Command("dealer", "dealer-grade commands");
dealerCommand.Add(dealerGradeCommand);
rootCommand.Add(dealerCommand);

var budgetCommand = new Command("budget", "the fixed monthly running cost, then the payment room and max purchase price under the scenario for each target monthly budget");
budgetCommand.Add(scenarioOption);
budgetCommand.Add(fulfillmentOption);
budgetCommand.SetAction(async (parseResult, cancellationToken) =>
{
    string scenarioPath = parseResult.GetValue(scenarioOption)!;
    Fulfillment? fulfillment = parseResult.GetValue(fulfillmentOption);
    return await BudgetCommand.RunAsync(scenarioPath, fulfillment, cancellationToken);
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
