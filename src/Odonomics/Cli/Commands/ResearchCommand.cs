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
    /// <summary>Paced only between vehicles that actually hit NHTSA or Marketcheck this run: a
    /// cached vehicle makes no network call, so pausing after one too would only slow down a batch
    /// where most of the filtered set is already cached, for no benefit to either API.</summary>
    private static readonly TimeSpan PauseBetweenVehicles = TimeSpan.FromSeconds(1);

    public static async Task<int> RunAsync(string scenarioPath, IReadOnlyList<string> vins, bool refresh, bool quiet, CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();

        List<VehicleEntity> allVehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
            .Include(v => v.VinRecord)
            .Include(v => v.AuctionCheck)
            .ToListAsync(cancellationToken);

        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        // Explicit VINs need no scenario, so the fee flag's posting is chosen under delivery unless one was loaded,
        // and no zip or radius is known to judge a CarMax "Only at" posting's distance by.
        Fulfillment fulfillment = Fulfillment.Delivery;
        string? zip = null;
        int? radiusMiles = null;
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
            fulfillment = scenario.Fulfillment;
            zip = scenario.Zip;
            radiusMiles = scenario.RadiusMiles;
            vehicles = SelectVehiclesToResearch(allVehicles, scenario, latestCoverageBySource);
        }

        if (vehicles.Count == 0)
        {
            AnsiConsole.MarkupLine("no vehicles to research");
            return 0;
        }

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var marketcheck = new MarketcheckHistoryClient(secrets.MarketcheckApiKey, http);
        var researchService = new VinResearchService(new NhtsaClient(http), marketcheck);

        var summaryEntries = new List<ResearchSummaryEntry>();
        int fullyResearched = 0;
        int partiallyResearched = 0;
        int unreachable = 0;
        int fetchedCount = 0;
        int cachedCount = 0;

        // Counts a vehicle whose Marketcheck call was skipped outright because
        // MarketcheckHistoryClient.AllowanceExhausted already tripped (three vehicles in this run hit
        // HTTP 429 in a row), so the one-line notice below can name how many vehicles were affected
        // instead of every one of them getting its own silent "partial" tag.
        int marketcheckAllowanceSkipped = 0;

        try
        {
            for (int i = 0; i < vehicles.Count; i++)
            {
                VehicleEntity vehicle = vehicles[i];
                string label = $"{vehicle.Year} {vehicle.Make} {vehicle.Model} ({vehicle.Vin})";
                bool usingCache = !VinResearchService.NeedsRefresh(vehicle.VinRecord, refresh);
                try
                {
                    VinResearchResult research = usingCache
                        ? VinResearchService.FromCached(vehicle.VinRecord!)
                        : await researchService.RefreshAsync(db, vehicle, refresh, cancellationToken);

                    if (!usingCache && research.History.CouldNotFetchReason == MarketcheckHistoryClient.AllowanceExhaustedReason)
                    {
                        marketcheckAllowanceSkipped++;
                    }

                    decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
                    IReadOnlyList<RedFlag> redFlags = [.. VinResearchService.RedFlags(research, currentPrice), .. FeeRedFlags.For(vehicle, latestCoverageBySource, fulfillment, zip, radiusMiles), .. AuctionChecks.RedFlags(vehicle.AuctionCheck)];

                    bool anyPieceFailed = IsPartiallyResearched(research);

                    // A "partial" tag alongside the real flags so a vehicle whose recalls, complaints,
                    // safety-ratings, or Marketcheck VIN history fetch failed this run never reads as
                    // indistinguishable from one that was actually checked and came back clean.
                    List<string> tags = [.. redFlags.Select(f => f.ShortTag)];
                    if (anyPieceFailed)
                    {
                        tags.Add("partial");
                    }

                    ResearchSource source = usingCache ? ResearchSource.Cached : ResearchSource.Fetched;
                    summaryEntries.Add(new ResearchSummaryEntry(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Vin, tags, source));
                    if (usingCache)
                    {
                        cachedCount++;
                    }
                    else
                    {
                        fetchedCount++;
                    }

                    if (!anyPieceFailed)
                    {
                        // Cached vehicles never contribute here: they were not researched this run, so
                        // counting them would let a cache-only run masquerade as work actually done, and
                        // would make the "everything unreachable" exit code below unreachable whenever any
                        // vehicle in the batch happened to be cached.
                        if (!usingCache)
                        {
                            fullyResearched++;
                        }
                        if (!quiet && !usingCache)
                        {
                            AnsiConsole.WriteLine(ResearchProgressLine.Format(label, tags));
                        }
                    }
                    else
                    {
                        partiallyResearched++;
                        if (!quiet)
                        {
                            string sentence = ComposePartialResultSentence(research.Safety, research.Recalls, research.Complaints, research.History);
                            AnsiConsole.MarkupLineInterpolated($"[yellow]{label}: {sentence}[/]");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    unreachable++;
                    summaryEntries.Add(new ResearchSummaryEntry(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Vin, ["unreachable"], ResearchSource.Unreachable));
                    if (!quiet)
                    {
                        AnsiConsole.MarkupLineInterpolated($"[red]{label}: could not be reached ({ex.Message})[/]");
                    }
                }

                if (quiet)
                {
                    ResearchProgressCounter.Write(AnsiConsole.Console, i + 1, vehicles.Count, fetchedCount, cachedCount, unreachable);
                }

                if (i < vehicles.Count - 1 && !usingCache)
                {
                    await Task.Delay(PauseBetweenVehicles, cancellationToken);
                }
            }
        }
        finally
        {
            // Runs on every exit from the loop above, including a cancellation that unwinds straight
            // through the Task.Delay above (the catch above deliberately lets that one propagate), so
            // Ctrl-C during a --quiet run never leaves the cursor parked mid-counter-line.
            if (quiet)
            {
                ResearchProgressCounter.Finish(AnsiConsole.Console);
            }
        }

        if (marketcheckAllowanceSkipped > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLineInterpolated($"[yellow]Marketcheck VIN history hit HTTP 429 for {MarketcheckHistoryClient.ConsecutiveFailuresBeforeAllowanceExhausted} vehicles in a row; treated the allowance as exhausted and skipped Marketcheck for {marketcheckAllowanceSkipped} vehicle(s) for the rest of this run. A later plain odo research will retry them.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"{fullyResearched} fully researched, {partiallyResearched} partially researched, {unreachable} unreachable");

        AnsiConsole.WriteLine();
        ResearchSummaryRenderer.Render(AnsiConsole.Console, summaryEntries);

        // Every vehicle in the batch cached, none unreachable, is a normal cache-hit run, not a
        // failure: only when at least one vehicle was unreachable AND no fetch attempt in the batch
        // came back with any data (fully or partially) does this signal the total-outage case the
        // exit code exists to catch.
        return unreachable > 0 && fullyResearched + partiallyResearched == 0 ? 1 : 0;
    }

    /// <summary>Whether this vehicle's research run counts as partial rather than fully researched:
    /// true when any of recalls, complaints, safety ratings, or the Marketcheck VIN history could not
    /// be fetched this run, including a history fetch that failed (HTTP 429 or otherwise) even though
    /// every NHTSA piece came back clean. A separate seam from <see cref="RunAsync"/>, the same as
    /// <see cref="SelectVehiclesToResearch"/>, so a test can assert this without a database or network
    /// call.</summary>
    public static bool IsPartiallyResearched(VinResearchResult research) =>
        research.Recalls.CouldNotFetchReason is not null
        || research.Complaints.CouldNotFetchReason is not null
        || research.Safety.CouldNotFetchReason is not null
        || research.History.CouldNotFetchReason is not null;

    /// <summary>The sentence for a vehicle whose safety-ratings, recalls, complaints, or Marketcheck
    /// VIN history call could not be fetched this run (see NhtsaClient's and MarketcheckHistoryClient's
    /// retry-then-degrade behavior): every failed piece's reason, then which of the four still came
    /// back and were stored. For example, a safety-ratings failure with recalls and complaints intact
    /// reads "NHTSA safety ratings could not be fetched, HTTP 200 with an HTML error page; recalls and
    /// complaints stored".</summary>
    private static string ComposePartialResultSentence(SafetyRatingsResult safety, RecallsResult recalls, ComplaintsResult complaints, VinHistoryResult history)
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

        if (history.CouldNotFetchReason is string historyReason)
        {
            failedClauses.Add(historyReason);
        }
        else
        {
            succeededNames.Add("VIN history");
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

    /// <summary>Every vehicle that passes the scenario's filters, whether or not it needs a refresh:
    /// NeedsRefresh is decided per vehicle inside the research loop, where it also picks the
    /// fetch-vs-cache path. Filtering it out here too would drop an already-cached vehicle from the
    /// summary entirely, which is the whole reason the summary covers the whole filtered set instead
    /// of only the vehicles fetched that run. A separate seam from <see cref="RunAsync"/> so a test
    /// can assert an already-fresh vehicle is still selected without a database or network call.</summary>
    public static List<VehicleEntity> SelectVehiclesToResearch(
        IReadOnlyList<VehicleEntity> allVehicles,
        Scenario scenario,
        IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        [.. allVehicles.Where(v => PassesScenarioFilters(v, scenario, latestCoverageBySource))];

    /// <summary>The scenario's non-price hard filters (allowed model, minimum year, maximum mileage, not new
    /// stock) plus the price ceiling, checked against the same cheapest-to-take-home posting `odo rank` scores
    /// (see <see cref="RankCommand.ForScoring"/>), asking price and shipping fee together, so a car `odo rank`
    /// would exclude for its price is never researched either. Missing-price is excluded on purpose, and so is
    /// every posting being reserved, in transit, or an out-of-radius CarMax "Only at" posting: a vehicle in
    /// any of those shapes is still worth researching (safety ratings and VIN history don't depend on today's
    /// availability), and <see cref="Scorer.FilterReasons"/> would otherwise reject it outright. The
    /// out-of-radius shape gets one more check first: it is exactly the shape that makes
    /// <see cref="VehicleForScoring.LowestCurrentPrice"/> null (nothing is purchasable to price it from), which
    /// would otherwise silently skip the price ceiling too and spend a research call on a car that could never
    /// pass it, so that ceiling is checked here against the store's own asking price before the "out of radius"
    /// tolerance below lets the vehicle through.</summary>
    private static bool PassesScenarioFilters(VehicleEntity vehicle, Scenario scenario, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles);
        if (forScoring.OnlyAtOutOfRadiusStore is not null && ExceedsMaxPriceIgnoringOutOfRadius(vehicle, scenario, latestCoverageBySource))
        {
            return false;
        }

        return Scorer.FilterReasons(forScoring, scenario).All(reason =>
            reason.Contains("no current asking price")
            || reason.Contains("reserved for another buyer or in transit")
            || reason.Contains("out of radius"));
    }

    /// <summary>True when the scenario has a price ceiling and the cheapest posting `odo rank` would have
    /// priced this vehicle from if its out-of-radius CarMax "Only at" posting still counted as purchasable
    /// (see <see cref="VehiclePricing.LowestPriceIncludingOutOfRadius"/>) costs more than it, the same
    /// asking-plus-shipping formula <see cref="Scorer.FilterReasons"/> uses. False when the scenario sets no
    /// ceiling or no such posting has a price to compare.</summary>
    private static bool ExceedsMaxPriceIgnoringOutOfRadius(VehicleEntity vehicle, Scenario scenario, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        if (scenario.Filters.MaxPrice is not int maxPrice)
        {
            return false;
        }

        PurchasePrice? price = VehiclePricing.LowestPriceIncludingOutOfRadius(vehicle, latestCoverageBySource, scenario.Fulfillment);
        if (price is not PurchasePrice purchasePrice)
        {
            return false;
        }

        decimal priceWithShipping = purchasePrice.Asking + (purchasePrice.ShippingIncluded ? 0m : purchasePrice.ShippingFee ?? 0m);
        return priceWithShipping > maxPrice;
    }
}
