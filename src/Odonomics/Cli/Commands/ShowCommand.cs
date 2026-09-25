using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;
using Odonomics.Secrets;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class ShowCommand
{
    public static async Task<int> RunAsync(string vin, string scenarioPath, bool refresh, bool allHistory, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        using OdonomicsDbContext db = LedgerFactory.Open();

        VehicleEntity? vehicle = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
            .Include(v => v.Notes)
            .Include(v => v.VinRecord)
            .FirstOrDefaultAsync(v => v.Vin == vin, cancellationToken);

        if (vehicle is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
            return 1;
        }

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var researchService = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient(secrets.MarketcheckApiKey, http));

        VinResearchResult research = VinResearchService.NeedsRefresh(vehicle.VinRecord, refresh)
            ? await researchService.RefreshAsync(db, vehicle, refresh, cancellationToken)
            : VinResearchService.FromCached(vehicle.VinRecord!);

        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        // The red flags compare asking prices with other listings' asking prices, so they keep the
        // asking price alone; the monthly cost is what taking the car home costs, shipping included.
        decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
        IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlags(research, currentPrice);

        PurchasePrice? purchasePrice = VehiclePricing.LowestCurrentPurchasePrice(vehicle, latestCoverageBySource);
        (CostBreakdown? monthlyCost, string? unavailable) = MonthlyCostFor($"{vehicle.Make} {vehicle.Model}", purchasePrice?.Total, scenario);
        ShowRenderer.Render(vehicle, research, redFlags, allHistory, monthlyCost, unavailable, purchasePrice);
        return 0;
    }

    /// <summary>Prices one vehicle the way `odo rank` does (see <see cref="Scorer.ComputeCost"/>), but
    /// without the hard filters: `odo show` is asked about any VIN in the ledger, including one the
    /// scenario would exclude, and its monthly cost is still worth seeing. What it cannot price it
    /// explains instead of throwing: a vehicle with no current price, or a model the scenario has no
    /// insurance or mpg figure for. <paramref name="purchasePrice"/> is what the car costs to take
    /// home, its asking price plus any shipping fee (see <see cref="PurchasePrice.Total"/>).</summary>
    public static (CostBreakdown? Cost, string? Unavailable) MonthlyCostFor(string makeModel, decimal? purchasePrice, Scenario scenario)
    {
        if (purchasePrice is not decimal price)
        {
            return (null, "no current asking price (every posting is gone)");
        }

        if (scenario.InsuranceMonthlyByModel.GetValueOrDefault(makeModel) is not decimal insuranceMonthly)
        {
            return (null, $"the scenario has no insurance figure for {makeModel}");
        }

        return scenario.MpgByModel.TryGetValue(makeModel, out decimal mpg)
            ? (Scorer.ComputeCost(price, insuranceMonthly, mpg, scenario), null)
            : (null, $"the scenario has no mpg figure for {makeModel}");
    }
}
