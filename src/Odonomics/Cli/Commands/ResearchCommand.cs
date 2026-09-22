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

        var flagged = new List<(VehicleEntity Vehicle, IReadOnlyList<string> Flags)>();
        int failures = 0;

        for (int i = 0; i < vehicles.Count; i++)
        {
            VehicleEntity vehicle = vehicles[i];
            string label = $"{vehicle.Vin} ({vehicle.Year} {vehicle.Make} {vehicle.Model})";
            try
            {
                bool usingCache = !VinResearchService.NeedsRefresh(vehicle.VinRecord, refresh);
                VinResearchResult research = usingCache
                    ? VinResearchService.FromCached(vehicle.VinRecord!)
                    : await researchService.RefreshAsync(db, vehicle, refresh, cancellationToken);

                decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
                IReadOnlyList<string> redFlags = VinResearchService.RedFlags(research, currentPrice);
                if (redFlags.Count > 0)
                {
                    flagged.Add((vehicle, redFlags));
                }

                string source = usingCache ? "cached" : "researched";
                string flagSummary = redFlags.Count == 0 ? "no red flags" : $"{redFlags.Count} red flag(s)";
                AnsiConsole.MarkupLineInterpolated($"{label}: {source}, {flagSummary}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failures++;
                AnsiConsole.MarkupLineInterpolated($"[red]{label}: failed to research ({ex.Message})[/]");
            }

            if (i < vehicles.Count - 1)
            {
                await Task.Delay(PauseBetweenVehicles, cancellationToken);
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"[bold]Red flags ({flagged.Count} of {vehicles.Count} researched)[/]");
        if (flagged.Count == 0)
        {
            AnsiConsole.MarkupLine("  none found");
        }
        else
        {
            foreach ((VehicleEntity vehicle, IReadOnlyList<string> flags) in flagged)
            {
                AnsiConsole.MarkupLineInterpolated($"  {vehicle.Vin} ({vehicle.Year} {vehicle.Make} {vehicle.Model}):");
                foreach (string flag in flags)
                {
                    AnsiConsole.MarkupLineInterpolated($"    [red]- {flag}[/]");
                }
            }
        }

        return failures == 0 ? 0 : 1;
    }

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
