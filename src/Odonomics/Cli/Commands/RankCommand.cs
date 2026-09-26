using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class RankCommand
{
    public static async Task<int> RunAsync(string scenarioPath, decimal? budget, int? term, bool detail, Fulfillment? fulfillment, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        if (term is int overrideTerm)
        {
            scenario = scenario with { TermMonths = overrideTerm };
        }

        if (fulfillment is Fulfillment overrideFulfillment)
        {
            scenario = scenario with { Fulfillment = overrideFulfillment };
        }

        using OdonomicsDbContext db = LedgerFactory.Open();

        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        List<VehicleEntity> vehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
            .Include(v => v.Postings).ThenInclude(p => p.Attributes)
            .ToListAsync(cancellationToken);

        List<VinRecordEntity> vinRecords = await db.VinRecords.ToListAsync(cancellationToken);
        Dictionary<string, VinRecordEntity> vinRecordsByVin = vinRecords.ToDictionary(r => r.Vin);

        var scores = new List<Score>();
        var research = new Dictionary<string, ResearchStatus>();
        foreach (VehicleEntity vehicle in vehicles)
        {
            VehicleForScoring forScoring = ForScoring(vehicle, latestCoverageBySource, scenario.Fulfillment);
            scores.Add(Scorer.Score(forScoring, scenario));
            research[vehicle.Vin] = ResearchStatusFor(
                vinRecordsByVin.GetValueOrDefault(vehicle.Vin),
                VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource),
                FeeRedFlags.For(vehicle, latestCoverageBySource, scenario.Fulfillment));
        }

        RankRenderer.Render(AnsiConsole.Console, scores, budget, research, scenario.TargetMonthlyBudgets, detail);
        return 0;
    }

    /// <summary>The slice of a ledger vehicle the scorer reads, plus the site badge rank shows beside
    /// it. The price, the fees, and the badge all come from the same posting: the cheapest one to take
    /// home under <paramref name="fulfillment"/> (see <see cref="VehiclePricing.LowestCurrentPurchasePosting"/>).</summary>
    public static VehicleForScoring ForScoring(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment)
    {
        PurchasePrice? purchasePrice = VehiclePricing.LowestCurrentPurchasePrice(vehicle, latestCoverageBySource, fulfillment);
        PostingEntity? cheapest = VehiclePricing.LowestCurrentPurchasePosting(vehicle, latestCoverageBySource, fulfillment);
        return new VehicleForScoring
        {
            Vin = vehicle.Vin,
            Year = vehicle.Year,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            LowestCurrentPrice = purchasePrice?.Asking,
            ShippingFee = purchasePrice?.ShippingFee,
            PickupFee = purchasePrice?.PickupFee,
            PickupLocation = purchasePrice?.PickupLocation,
            Fulfillment = fulfillment,
            ItemizedFees = purchasePrice?.ItemizedFees,
            FeePosture = purchasePrice?.FeePosture,
            DealerGrade = DealerGradeSummary(vehicle),
            OnlyFGradedDealers = vehicle.Postings.Count > 0 && vehicle.Postings.All(p => p.Dealer?.Grade?.StartsWith('F') == true),
            SiteBadge = cheapest is null ? null : SiteBadgeText.For(cheapest.Attributes),
        };
    }

    /// <summary>The research column's status for one vehicle. <paramref name="listingFlags"/> are the flags
    /// its postings raise (see <see cref="FeeRedFlags"/>), which need no research, so a vehicle that has
    /// not been researched yet still reports one.</summary>
    private static ResearchStatus ResearchStatusFor(VinRecordEntity? record, decimal? currentPrice, IReadOnlyList<RedFlag> listingFlags)
    {
        if (record?.ResearchedAt is not DateTimeOffset researchedAt)
        {
            return new ResearchStatus(Researched: false, ResearchedAt: null, HasRedFlag: listingFlags.Count > 0, RecallsKnown: false, RecallCount: 0);
        }

        IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlagsForCached(record, currentPrice);
        return new ResearchStatus(
            Researched: true,
            ResearchedAt: researchedAt,
            HasRedFlag: redFlags.Count + listingFlags.Count > 0,
            RecallsKnown: record.RecallsRawJson is not null,
            RecallCount: record.OpenRecallCount);
    }

    private static string? DealerGradeSummary(VehicleEntity vehicle)
    {
        List<string> grades = [.. vehicle.Postings
            .Select(p => p.Dealer?.Grade)
            .OfType<string>()
            .Distinct()];
        return grades.Count == 0
            ? null
            : string.Join("/", grades);
    }
}
