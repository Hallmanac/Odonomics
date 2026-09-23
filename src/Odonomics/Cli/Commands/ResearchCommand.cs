using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;
using Odonomics.Secrets;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class ResearchCommand
{
    private static readonly TimeSpan PauseBetweenVehicles = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(string scenarioPath, IReadOnlyList<string> vins, bool refresh, CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();

        List<VehicleEntity> allVehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.VinRecord)
            .ToListAsync(cancellationToken);

        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        List<VehicleEntity> vehicles;
        if (vins.Count > 0)
        {
            vehicles = [];
            foreach (string vin in vins)
            {
                VehicleEntity? vehicle = allVehicles.FirstOrDefault(v => v.Vin == vin);
                if (vehicle is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
                    continue;
                }

                vehicles.Add(vehicle);
            }
        }
        else
        {
            Scenario scenario = ScenarioLoader.Load(scenarioPath);
            vehicles = [.. allVehicles.Where(v =>
                PassesScenarioFilters(v, scenario, VehiclePricing.LowestCurrentPrice(v, latestCoverageBySource))
                && VinResearchService.NeedsRefresh(v.VinRecord, refresh))];
        }

        if (vehicles.Count == 0)
        {
            AnsiConsole.MarkupLine("no vehicles to research");
            return 0;
        }

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var researchService = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient(secrets.MarketcheckApiKey, http));

        var summaryEntries = new List<ResearchSummaryEntry>();
        int fullyResearched = 0;
        int partiallyResearched = 0;
        int unreachable = 0;

        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleEntity vehicle = vehicles[i];
            string label = $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.Vin})";
            try
            {
                bool usingCache = !VinResearchService.NeedsRefresh(vehicle.VinRecord, refresh);
                VinResearchResult research = usingCache
                    ? VinResearchService.FromCached(vehicle.VinRecord!)
                    : await researchService.RefreshAsync(db, vehicle, refresh, cancellationToken);

                decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
                IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlags(research, currentPrice);

                bool anyPieceFailed = research.Recalls.CouldNotFetchReason is not null
                    || research.Complaints.CouldNotFetchReason is not null
                    || research.Safety.CouldNotFetchReason is not null;

                // A "partial" tag alongside the real flags so a vehicle whose recalls, complaints, or
                // safety-ratings fetch failed this run never reads as indistinguishable from one that
                // was actually checked and came back clean.
                List<string> tags = [.. redFlags.Select(f => f.ShortTag)];
                if (anyPieceFailed)
                {
                    tags.Add("partial");
                }

                summaryEntries.Add(new ResearchSummaryEntry(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Vin, tags));

                if (!anyPieceFailed)
                {
                    fullyResearched++;
                    string source = usingCache ? "cached" : "researched";
                    string flagSummary = redFlags.Count == 0 ? "no red flags" : $"{redFlags.Count} red flag(s)";
                    AnsiConsole.MarkupLineInterpolated($"{label}: {source}, {flagSummary}");
                }
                else
                {
                    partiallyResearched++;
                    string sentence = ComposePartialResultSentence(research.Safety, research.Recalls, research.Complaints);
                    AnsiConsole.MarkupLineInterpolated($"[yellow]{label}: {sentence}[/]");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                unreachable++;
                summaryEntries.Add(new ResearchSummaryEntry(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Vin, ["unreachable"]));
                AnsiConsole.MarkupLineInterpolated($"[red]{label}: could not be reached ({ex.Message})[/]");
            }

            if (i < vehicles.Count - 1)
            {
                await Task.Delay(PauseBetweenVehicles, cancellationToken);
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"{fullyResearched} fully researched, {partiallyResearched} partially researched, {unreachable} unreachable");

        AnsiConsole.WriteLine();
        ResearchSummaryRenderer.Render(AnsiConsole.Console, summaryEntries);

        return fullyResearched + partiallyResearched == 0 ? 1 : 0;
    }

    /// <summary>The sentence for a vehicle whose safety-ratings, recalls, or complaints call could
    /// not be fetched this run (see NhtsaClient's retry-then-degrade behavior): every failed piece's
    /// reason, then which of the three still came back and were stored. For example, a safety-ratings
    /// failure with recalls and complaints intact reads "NHTSA safety ratings could not be fetched,
    /// HTTP 200 with an HTML error page; recalls and complaints stored".</summary>
    private static string ComposePartialResultSentence(SafetyRatingsResult safety, RecallsResult recalls, ComplaintsResult complaints)
    {
        List<string> failedClauses = [];
        List<string> succeededNames = [];

        if (safety.CouldNotFetchReason is string safetyReason)
        {
            failedClauses.Add(safetyReason);
        }
        else
        {
            succeededNames.Add("safety ratings");
        }

        if (recalls.CouldNotFetchReason is string recallsReason)
        {
            failedClauses.Add(recallsReason);
        }
        else
        {
            succeededNames.Add("recalls");
        }

        if (complaints.CouldNotFetchReason is string complaintsReason)
        {
            failedClauses.Add(complaintsReason);
        }
        else
        {
            succeededNames.Add("complaints");
        }

        string storedClause = succeededNames.Count == 0 ? "nothing else stored" : $"{JoinWithAnd(succeededNames)} stored";
        return $"{string.Join("; ", failedClauses)}; {storedClause}";
    }

    private static string JoinWithAnd(IReadOnlyList<string> items) => items.Count switch
    {
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}",
    };

    /// <summary>The scenario's non-price hard filters (allowed model, minimum year, maximum mileage,
    /// not new stock). Missing-price is excluded on purpose: a vehicle that has no current asking
    /// price is still worth researching (safety ratings and VIN history don't depend on today's
    /// price), and <see cref="Scorer.FilterReasons"/> would otherwise reject every unpriced vehicle
    /// outright.</summary>
    private static bool PassesScenarioFilters(VehicleEntity vehicle, Scenario scenario, decimal? currentPrice)
    {
        var forScoring = new VehicleForScoring
        {
            Vin = vehicle.Vin,
            Year = vehicle.Year,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            LowestCurrentPrice = currentPrice,
        };

        return Scorer.FilterReasons(forScoring, scenario).All(reason => reason.Contains("no current asking price"));
    }
}
