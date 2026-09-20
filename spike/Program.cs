using System.Text.Json;
using Spike;
using Spike.Models;
using Spike.Reporting;
using Spike.Sources;

var repoRoot = Directory.GetCurrentDirectory();
var spikeDir = Path.Combine(repoRoot, "spike");
if (!Directory.Exists(spikeDir))
{
    Console.Error.WriteLine("Run this from the repo root: dotnet run --project spike");
    return 1;
}

var config = new Config();
SecretRedactor.Register(config.AutoDevApiKey, config.MarketcheckApiKey);

var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
var cancellationToken = cts.Token;

// `dotnet run --project spike -- --diag <source>` runs one source into a scratch folder without
// advancing the day counter or touching SPIKE-FINDINGS.md. Build-time debugging only; the single
// command that runs a real day is `dotnet run --project spike` with no arguments.
if (args.Length > 0 && args[0] == "--diag")
{
    var diagRecorded = new RecordedResponses(repoRoot, "_diag");
    var diagExtraction = new ExtractionClient(
        Path.Combine(spikeDir, "extraction", "prompt.md"),
        Path.Combine(spikeDir, "extraction", "schema.json"));
    var diagProfileRoot = Path.Combine(spikeDir, ".profiles");
    IListingSource diagSource = args[1] switch
    {
        "cars.com" => PageWalkSources.CreateCarsCom(Path.Combine(diagProfileRoot, "diag-cars.com"), diagRecorded, diagExtraction),
        "autotrader" => PageWalkSources.CreateAutotrader(Path.Combine(diagProfileRoot, "diag-autotrader"), diagRecorded, diagExtraction),
        "carvana" => PageWalkSources.CreateCarvana(Path.Combine(diagProfileRoot, "diag-carvana"), diagRecorded, diagExtraction),
        "craigslist" => new CraigslistSource(diagRecorded, diagExtraction),
        _ => throw new ArgumentException($"unknown diag source {args[1]}"),
    };
    var diagResult = await diagSource.RunAsync(cancellationToken);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        diagResult.Source,
        diagResult.CouldNotRun,
        diagResult.CouldNotRunReason,
        diagResult.Failures,
        Candidates = diagResult.Candidates,
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var (day, runName) = RunState.NextRun(repoRoot);
Console.WriteLine($"=== Odonomics spike run: day {day} ({runName}) ===");

var recorded = new RecordedResponses(repoRoot, runName);
var extraction = new ExtractionClient(
    Path.Combine(spikeDir, "extraction", "prompt.md"),
    Path.Combine(spikeDir, "extraction", "schema.json"));

var results = new List<SourceRunResult>();

async Task RunSourceAsync(string label, Func<CancellationToken, Task<SourceRunResult>> run)
{
    Console.WriteLine($"--- {label} ---");
    try
    {
        var result = await run(cancellationToken);
        results.Add(result);
        Console.WriteLine(result.CouldNotRun
            ? $"{label}: could not run ({result.CouldNotRunReason})"
            : $"{label}: {result.CandidatesFound} candidates, {result.CandidatesWithVin} with VIN, {result.Failures.Count} failures, {result.WallTime.TotalSeconds:0}s, ${result.DollarsSpent:0.00}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{label}: crashed: {ex.Message}");
        results.Add(new SourceRunResult { Source = label, CouldNotRun = true, CouldNotRunReason = $"crashed: {ex.Message}" });
    }
}

await RunSourceAsync("auto.dev", ct => new AutoDevSource(config.AutoDevApiKey, recorded).RunAsync(ct));
await RunSourceAsync("marketcheck", ct => new MarketcheckSource(config.MarketcheckApiKey, recorded).RunAsync(ct));

var profileRoot = Path.Combine(spikeDir, ".profiles");
await RunSourceAsync("cars.com (autotrader fallback)", ct => PageWalkSources.RunAggregatorAsync(profileRoot, recorded, extraction, ct));
await RunSourceAsync("carvana", ct => PageWalkSources.CreateCarvana(Path.Combine(profileRoot, "carvana"), recorded, extraction).RunAsync(ct));
await RunSourceAsync("craigslist", ct => new CraigslistSource(recorded, extraction).RunAsync(ct));

// VINs unique to a source: a VIN this source found that no other source in this run also found.
var vinsBySource = results
    .Where(r => !r.CouldNotRun)
    .ToDictionary(r => r.Source, r => r.Candidates
        .Where(c => c.MatchesQuery && !string.IsNullOrWhiteSpace(c.Vin))
        .Select(c => c.Vin!.Trim().ToUpperInvariant())
        .ToHashSet());

foreach (var result in results.Where(r => !r.CouldNotRun))
{
    var ownVins = vinsBySource[result.Source];
    var seenElsewhere = vinsBySource
        .Where(kv => kv.Key != result.Source)
        .SelectMany(kv => kv.Value)
        .ToHashSet();
    result.VinsUniqueToSource = ownVins.Count(v => !seenElsewhere.Contains(v));
}

var findingsPath = Path.Combine(repoRoot, "SPIKE-FINDINGS.md");
await FindingsAppender.AppendDayResultsAsync(findingsPath, day, results, cancellationToken);

var dumpPath = await recorded.WriteAsync(
    "_all",
    "candidates.json",
    JsonSerializer.Serialize(results.Select(r => new
    {
        r.Source,
        r.CouldNotRun,
        r.CouldNotRunReason,
        r.WallTime,
        r.DollarsSpent,
        r.Failures,
        Candidates = r.Candidates,
    }), new JsonSerializerOptions { WriteIndented = true }),
    cancellationToken);

Console.WriteLine($"=== Done. Rows appended to {findingsPath}. Raw candidate dump: {dumpPath} ===");
return 0;
