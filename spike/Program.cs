using System.Text.Json;
using Spike;
using Spike.Models;
using Spike.Reporting;
using Spike.Sources;

string repoRoot = Directory.GetCurrentDirectory();
string spikeDir = Path.Combine(repoRoot, "spike");
if (!Directory.Exists(spikeDir))
{
    Console.Error.WriteLine("Run this from the repo root: dotnet run --project spike");
    return 1;
}

var config = new Config();
SecretRedactor.Register(config.AutoDevApiKey, config.MarketcheckApiKey);

var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
CancellationToken cancellationToken = cts.Token;

// `dotnet run --project spike -- --diag <source>` runs one source into a scratch folder without
// advancing the day counter or touching SPIKE-FINDINGS.md. Build-time debugging only; the single
// command that runs a real day is `dotnet run --project spike` with no arguments.
if (args.Length > 0 && args[0] == "--diag")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: dotnet run --project spike -- --diag <cars.com|autotrader|carvana|craigslist>");
        return 1;
    }

    var diagRecorded = new RecordedResponses(repoRoot, "_diag");
    var diagExtraction = new ExtractionClient(
        Path.Combine(spikeDir, "extraction", "prompt.md"),
        Path.Combine(spikeDir, "extraction", "schema.json"));
    string diagProfileRoot = Path.Combine(spikeDir, ".profiles");
    IListingSource diagSource = args[1] switch
    {
        "cars.com" => PageWalkSources.CreateCarsCom(Path.Combine(diagProfileRoot, "diag-cars.com"), diagRecorded, diagExtraction),
        "autotrader" => PageWalkSources.CreateAutotrader(Path.Combine(diagProfileRoot, "diag-autotrader"), diagRecorded, diagExtraction),
        "carvana" => PageWalkSources.CreateCarvana(Path.Combine(diagProfileRoot, "diag-carvana"), diagRecorded, diagExtraction),
        "craigslist" => new CraigslistSource(diagRecorded, diagExtraction),
        _ => throw new ArgumentException($"unknown diag source {args[1]}"),
    };
    SourceRunResult diagResult = await diagSource.RunAsync(cancellationToken);
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

// Peek, not commit: the day number is only persisted once this run's results are actually written
// out below, so a crash, a cancellation, or a Ctrl-C here does not silently consume a day.
(int day, string runName) = RunState.Peek(repoRoot);
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
        SourceRunResult result = await run(cancellationToken);
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

string profileRoot = Path.Combine(spikeDir, ".profiles");
await RunSourceAsync("cars.com (autotrader fallback)", ct => PageWalkSources.RunAggregatorAsync(profileRoot, recorded, extraction, ct));
await RunSourceAsync("carvana", ct => PageWalkSources.CreateCarvana(Path.Combine(profileRoot, "carvana"), recorded, extraction).RunAsync(ct));
await RunSourceAsync("craigslist", ct => new CraigslistSource(recorded, extraction).RunAsync(ct));

// VINs unique to a source: a VIN this source found that no other source in this run also found.
Dictionary<string, HashSet<string>> vinsBySource = results
    .Where(r => !r.CouldNotRun)
    .ToDictionary(r => r.Source, r => r.Candidates
        .Where(c => c.MatchesQuery && !string.IsNullOrWhiteSpace(c.Vin))
        .Select(c => c.Vin!.Trim().ToUpperInvariant())
        .ToHashSet());

foreach (SourceRunResult result in results.Where(r => !r.CouldNotRun))
{
    HashSet<string> ownVins = vinsBySource[result.Source];
    HashSet<string> seenElsewhere = vinsBySource
        .Where(kv => kv.Key != result.Source)
        .SelectMany(kv => kv.Value)
        .ToHashSet();
    result.VinsUniqueToSource = ownVins.Count(v => !seenElsewhere.Contains(v));
}

// From here on, persistence is best-effort against whatever the run produced: if the 30-minute
// budget (cancellationToken) has already expired, every source above still reported (crashed rows
// included), and that is exactly the data these last two writes must not lose. CancellationToken.None
// is deliberate: a cancelled token here would throw out of File.ReadAllTextAsync before a single row
// could be appended, discarding the whole day's results including the sources that succeeded.
string findingsPath = Path.Combine(repoRoot, "SPIKE-FINDINGS.md");
await FindingsAppender.AppendDayResultsAsync(findingsPath, day, results, CancellationToken.None);

string dumpPath = await recorded.WriteAsync(
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
    CancellationToken.None);

// Only now, with the findings row and the raw dump both safely on disk, does this day count as
// spent; see RunState.Commit.
RunState.Commit(repoRoot, day);

Console.WriteLine($"=== Done. Rows appended to {findingsPath}. Raw candidate dump: {dumpPath} ===");
return 0;
