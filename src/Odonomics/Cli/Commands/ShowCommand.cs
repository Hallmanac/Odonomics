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
            .Include(v => v.Postings).ThenInclude(p => p.Attributes)
            .Include(v => v.Notes)
            .Include(v => v.VinRecord)
            .Include(v => v.AuctionCheck)
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
        // asking price alone; the monthly cost is what taking the car home costs under the scenario's fulfillment,
        // itemized fees included. The fee and title-brand flags come from the postings and the salvage-auction flag from
        // the stored auction lookup, not the research.
        decimal? currentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
        IReadOnlyList<RedFlag> redFlags = [.. VinResearchService.RedFlags(research, currentPrice), .. FeeRedFlags.For(vehicle, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles), .. AuctionChecks.RedFlags(vehicle.AuctionCheck), .. TitleBrandFlags.For(vehicle)];

        // The similar-price note compares against every vehicle in the ledger, so it needs them all, not just this
        // one. The dealers come too: a cars.com copy of a CarMax posting is told apart by its dealer's name, and
        // without it that copy would count as an active price here but not in `odo rank`.
        List<VehicleEntity> ledgerVehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
            .ToListAsync(cancellationToken);
        string? priceNote = VehiclePricing.SimilarPriceNotes(ledgerVehicles, latestCoverageBySource).GetValueOrDefault(vehicle.Vin);

        PurchasePrice? purchasePrice = VehiclePricing.LowestCurrentPurchasePrice(vehicle, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles);
        bool onlyReservedOrInTransit = VehiclePricing.OnlyReservedOrInTransit(vehicle, latestCoverageBySource, scenario.Zip, scenario.RadiusMiles);
        string? onlyAtOutOfRadiusStore = VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, latestCoverageBySource, scenario.Zip, scenario.RadiusMiles);
        bool onlyCarsComCarMaxPostings = VehiclePricing.OnlyCarsComCarMaxPostings(vehicle, latestCoverageBySource);
        (CostBreakdown? monthlyCost, string? unavailable) = CostOrReason($"{vehicle.Make} {vehicle.Model}", purchasePrice, onlyAtOutOfRadiusStore, onlyReservedOrInTransit, onlyCarsComCarMaxPostings, scenario);

        if (VehiclePricing.UnmeasuredOnlyAtStore(vehicle, latestCoverageBySource, scenario.Zip, scenario.RadiusMiles) is string unmeasuredStore)
        {
            AnsiConsole.MarkupLineInterpolated($"[yellow]only at {unmeasuredStore}: its distance from {scenario.Zip} is not in this project's curated store list, so it is being treated as in radius[/]");
        }

        ShowRenderer.Render(vehicle, research, redFlags, allHistory, monthlyCost, unavailable, purchasePrice, priceNote);
        return 0;
    }

    /// <summary>Why <paramref name="purchasePrice"/>'s vehicle can't be priced because of the scenario's own
    /// price ceiling (see <see cref="Domain.HardFilters.MaxPrice"/>), the same figure `odo rank` checks against
    /// (see <see cref="Scorer.FilterReasons"/>): its asking price plus its shipping fee, unless
    /// that fee is already in the asking price (see <see cref="PurchasePrice.ShippingIncluded"/>). Null when the
    /// scenario sets no ceiling, there's no current price to check, or the price is within it, so `odo show`'s
    /// cost line is unaffected until then.</summary>
    public static string? OverPriceCeiling(PurchasePrice? purchasePrice, int? maxPrice)
    {
        if (purchasePrice is not PurchasePrice price || maxPrice is not int ceiling)
        {
            return null;
        }

        decimal priceWithShipping = price.Asking + (price.ShippingIncluded ? 0m : price.ShippingFee ?? 0m);
        return priceWithShipping > ceiling
            ? $"price ${priceWithShipping:N0} exceeds the maximum ${ceiling:N0}"
            : null;
    }

    /// <summary>The monthly cost `odo show` prints for one vehicle, or the reason it can't be priced,
    /// checked in the order that reason should win: the scenario's own price ceiling first (see
    /// <see cref="OverPriceCeiling"/>), then whether every live posting is an out-of-radius CarMax "Only
    /// at" posting (see <see cref="VehiclePricing.OnlyAtOutOfRadiusStore"/>), then whether every live
    /// posting is reserved or in transit (see <see cref="VehiclePricing.OnlyReservedOrInTransit"/>), then
    /// whether the vehicle's only postings are cars.com copies of a CarMax listing (see
    /// <see cref="VehiclePricing.OnlyCarsComCarMaxPostings"/>) rather than <paramref name="purchasePrice"/>
    /// simply being null, since that null means something else, "no current asking price (every posting is
    /// gone)", only when none of those is also true. <see cref="MonthlyCostFor"/> checks the rest.</summary>
    public static (CostBreakdown? Cost, string? Unavailable) CostOrReason(string makeModel, PurchasePrice? purchasePrice, string? onlyAtOutOfRadiusStore, bool onlyReservedOrInTransit, bool onlyCarsComCarMaxPostings, Scenario scenario) =>
        OverPriceCeiling(purchasePrice, scenario.Filters.MaxPrice) is string ceilingReason
            ? (null, ceilingReason)
            : onlyAtOutOfRadiusStore is string store
                ? (null, $"only at {store}, out of radius")
                : onlyReservedOrInTransit
                    ? (null, "every posting is reserved for another buyer or in transit, not yet purchasable")
                    : onlyCarsComCarMaxPostings
                        ? (null, "no current asking price (only a cars.com copy of a CarMax posting; see the CarMax walk)")
                        : MonthlyCostFor(makeModel, purchasePrice?.Total, scenario);

    /// <summary>Prices one vehicle the way `odo rank` does (see <see cref="Scorer.ComputeCost"/>), but
    /// without the hard filters: `odo show` is asked about any VIN in the ledger, including one the
    /// scenario would exclude, and its monthly cost is still worth seeing. What it cannot price it
    /// explains instead of throwing: a vehicle with no current price, or a model the scenario has no
    /// insurance or mpg figure for. A Toyota Prius Prime lands here too, once <c>--revisit</c> has
    /// relabelled it: the scenario carries no insurance or mpg line for it at all now that it's
    /// excluded, so its monthly cost is never computed, the same as any other model missing that
    /// data. <paramref name="purchasePrice"/> is what the car costs to take home, its asking price
    /// plus the fee for the scenario's fulfillment and any itemized fees (see <see cref="PurchasePrice.Total"/>).
    /// <see cref="CostOrReason"/> checks the price ceiling, the reserved/in-transit case, and the
    /// cars.com-CarMax-only case ahead of this.</summary>
    public static (CostBreakdown? Cost, string? Unavailable) MonthlyCostFor(string makeModel, decimal? purchasePrice, Scenario scenario)
    {
        if (purchasePrice is not decimal price)
        {
            return (null, "no current asking price (every posting is gone)");
        }

        if (scenario.InsuranceMonthlyByModel.GetValueOrDefault(makeModel) is not decimal insuranceMonthly)
        {
            return (null, $"the scenario needs an insurance figure for {makeModel}");
        }

        return scenario.MpgByModel.GetValueOrDefault(makeModel) is decimal mpg
            ? (Scorer.ComputeCost(price, insuranceMonthly, mpg, scenario), null)
            : (null, $"the scenario needs an mpg figure for {makeModel}");
    }
}
