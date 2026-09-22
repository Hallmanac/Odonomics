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
    public static async Task<int> RunAsync(string vin, bool refresh, CancellationToken cancellationToken)
    {
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
        decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, RunSources.LatestCoverageBySource(runs));
        IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlags(research, currentPrice);

        ShowRenderer.Render(vehicle, research, redFlags);
        return 0;
    }
}
