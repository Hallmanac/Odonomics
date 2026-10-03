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
            .Include(v => v.AuctionCheck)
            .ToListAsync(cancellationToken);

        List<VinRecordEntity> vinRecords = await db.VinRecords.ToListAsync(cancellationToken);
        Dictionary<string, VinRecordEntity> vinRecordsByVin = vinRecords.ToDictionary(r => r.Vin);

        IReadOnlyDictionary<string, string> priceNotes = VehiclePricing.SimilarPriceNotes(vehicles, latestCoverageBySource);
        FactoryTrimTable trimTable = FactoryTrimTable.LoadShipped();

        var scores = new List<Score>();
        var research = new Dictionary<string, ResearchStatus>();
        foreach (VehicleEntity vehicle in vehicles)
        {
            VehicleForScoring forScoring = ForScoring(vehicle, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles, trimTable) with
            {
                PriceNote = priceNotes.GetValueOrDefault(vehicle.Vin),
            };
            scores.Add(Scorer.Score(forScoring, scenario));
            research[vehicle.Vin] = ResearchStatusFor(
                vinRecordsByVin.GetValueOrDefault(vehicle.Vin),
                VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource),
                [.. FeeRedFlags.For(vehicle, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles), .. AuctionChecks.RedFlags(vehicle.AuctionCheck), .. TitleBrandFlags.For(vehicle)]);
        }

        RankRenderer.Render(AnsiConsole.Console, scores, budget, research, scenario.TargetMonthlyBudgets, detail);
        return 0;
    }

    /// <summary>The slice of a ledger vehicle the scorer reads, plus the site badge rank shows beside
    /// it. The price, the fees, and the badge all come from the same posting: the cheapest purchasable
    /// one to take home under <paramref name="fulfillment"/> (see
    /// <see cref="VehiclePricing.LowestCurrentPurchasePosting"/>), never a reserved, in-transit, or
    /// out-of-radius "Only at" one. The availability note is the one exception: it comes from any of the
    /// vehicle's active postings, not only that cheapest one (see
    /// <see cref="VehiclePricing.ReservedOrInTransitNote"/>). <paramref name="zip"/> and
    /// <paramref name="radiusMiles"/> are the scenario's own, for judging whether a CarMax "Only at"
    /// posting's store is close enough to still purchase. The equipment is the vehicle's stored window-sticker
    /// status with <paramref name="trimTable"/> filling what it left unknown; null means no table, so only the
    /// stored statuses count. The salvage-auction sale is the vehicle's stored lookup's, so a caller must load
    /// <see cref="VehicleEntity.AuctionCheck"/> or the vehicle is never excluded for it.</summary>
    public static VehicleForScoring ForScoring(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment, string zip, int radiusMiles, FactoryTrimTable? trimTable = null)
    {
        PurchasePrice? purchasePrice = VehiclePricing.LowestCurrentPurchasePrice(vehicle, latestCoverageBySource, fulfillment, zip, radiusMiles);
        PostingEntity? cheapest = VehiclePricing.LowestCurrentPurchasePosting(vehicle, latestCoverageBySource, fulfillment, zip, radiusMiles);
        return new VehicleForScoring
        {
            Vin = vehicle.Vin,
            Year = vehicle.Year,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            LowestCurrentPrice = purchasePrice?.Asking,
            ShippingFee = purchasePrice?.ShippingFee,
            ShippingIncluded = purchasePrice?.ShippingIncluded ?? false,
            PickupFee = purchasePrice?.PickupFee,
            PickupLocation = purchasePrice?.PickupLocation,
            Fulfillment = fulfillment,
            ItemizedFees = purchasePrice?.ItemizedFees,
            FeePosture = purchasePrice?.FeePosture,
            DealerGrade = DealerGradeSummary(vehicle),
            OnlyFGradedDealers = vehicle.Postings.Count > 0 && vehicle.Postings.All(p => p.Dealer?.Grade?.StartsWith('F') == true),
            SiteBadge = cheapest is null ? null : SiteBadgeText.For(cheapest.Attributes),
            Availability = VehiclePricing.ReservedOrInTransitNote(vehicle, latestCoverageBySource),
            OnlyReservedOrInTransit = VehiclePricing.OnlyReservedOrInTransit(vehicle, latestCoverageBySource, zip, radiusMiles),
            OnlyAtOutOfRadiusStore = VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, latestCoverageBySource, zip, radiusMiles),
            UnmeasuredOnlyAtStore = VehiclePricing.UnmeasuredOnlyAtStore(vehicle, latestCoverageBySource, zip, radiusMiles),
            OnlyCarsComCarMaxPostings = VehiclePricing.OnlyCarsComCarMaxPostings(vehicle, latestCoverageBySource),
            SalvageAuctionSale = AuctionChecks.ExclusionSale(vehicle.AuctionCheck),
            Equipment = (trimTable ?? FactoryTrimTable.Empty).Fill(vehicle.StoredEquipment, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.Trim),
        };
    }

    /// <summary>The research column's status for one vehicle. <paramref name="listingFlags"/> are the flags
    /// its postings and its stored salvage-auction lookup raise (see <see cref="FeeRedFlags"/> and
    /// <see cref="AuctionChecks.RedFlags"/>), which need no research, so a vehicle that has
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
