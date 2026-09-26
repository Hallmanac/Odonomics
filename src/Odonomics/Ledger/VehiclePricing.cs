using Odonomics.Domain;

namespace Odonomics.Ledger;

public static class VehiclePricing
{
    /// <summary>The lowest latest price among this vehicle's postings that were still active as of
    /// the most recent full coverage of that posting's own source and model (LastSeen is that run's
    /// timestamp or later; see LedgerUpsertService and <see cref="RunSources.LatestCoverageBySource"/>).
    /// A posting whose source/model no run has ever covered, or that the latest full coverage saw, still
    /// counts, and so does one a later capped walk reached or never reached; only a posting whose
    /// source/model was fully checked more recently without seeing it again drops out.
    /// This is the asking price alone, which is what the price red flags compare; the cost model
    /// uses <see cref="LowestCurrentPurchasePrice"/>.</summary>
    public static decimal? LowestCurrentPrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        decimal?[] known = [.. ActivePostings(vehicle, latestCoverageBySource).Select(LatestAskingPrice).Where(p => p is not null)];
        return known.Length == 0 ? null : known.Min();
    }

    /// <summary>The cheapest way to take this vehicle home among its active postings (see
    /// <see cref="LowestCurrentPrice"/> for which those are): each posting's latest asking price
    /// plus the fee for <paramref name="fulfillment"/>, plus the fees it itemized on top of that price
    /// when its fee posture is itemized, so a far-away carvana car, or a dealer whose fees are added at
    /// the desk, is compared honestly with one that has neither. Under delivery the fee is the shipping
    /// fee; under pickup it is the pickup fee, or the shipping fee when no pickup fee was read (see
    /// <see cref="PurchasePrice"/>). A posting with a null fee costs its asking price, and so does one
    /// whose posture is all-in (its price already holds its fees), unknown (the page did not say), or
    /// never read. When two postings cost the same to take home, the one with the lower asking price is
    /// reported.</summary>
    public static PurchasePrice? LowestCurrentPurchasePrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment)?.Price;

    /// <summary>The active posting <see cref="LowestCurrentPurchasePrice"/> reports the price of, so a
    /// display that sits beside that price (a site's own badge for the listing, its dealer and fee
    /// posture) speaks for the same listing; null when no active posting has a price.</summary>
    public static PostingEntity? LowestCurrentPurchasePosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment)?.Posting;

    private static (PostingEntity Posting, PurchasePrice Price)? CheapestPosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment)
    {
        (PostingEntity Posting, PurchasePrice Price)[] known =
        [
            .. ActivePostings(vehicle, latestCoverageBySource)
                .Select(p => (Posting: p, Asking: LatestAskingPrice(p)))
                .Where(p => p.Asking is not null)
                .Select(p => (p.Posting, PurchasePriceOf(p.Posting, p.Asking.GetValueOrDefault(), fulfillment))),
        ];

        return known.Length == 0
            ? null
            : known.OrderBy(p => p.Price.Total).ThenBy(p => p.Price.Asking).First();
    }

    /// <summary>The posting's purchase price at <paramref name="asking"/>. Itemized fees count only when
    /// the posture is itemized: an all-in page's asking price already holds its fees, and adding an
    /// itemized total the page also printed would count them twice. That total is still carried, for
    /// display, as the fees an all-in price includes.</summary>
    private static PurchasePrice PurchasePriceOf(PostingEntity posting, decimal asking, Fulfillment fulfillment) =>
        posting.FeePosture switch
        {
            FeePostures.Itemized => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, posting.ItemizedFeesTotal, posting.FeePosture),
            FeePostures.AllIn => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture, posting.ItemizedFeesTotal),
            _ => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture),
        };

    private static IEnumerable<PostingEntity> ActivePostings(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        vehicle.Postings.Where(p =>
        {
            string key = RunSources.Key(p.Source, vehicle.Model);
            return !latestCoverageBySource.TryGetValue(key, out DateTimeOffset latestCoverage) || p.LastSeen >= latestCoverage;
        });

    /// <summary>The posting's most recent asking price, or null when that price is below
    /// <see cref="PlaceholderPrice.Floor"/>: a placeholder already on the ledger is history, never
    /// a price to rank or budget on, and it does not fall back to an older observation.</summary>
    private static decimal? LatestAskingPrice(PostingEntity posting) =>
        posting.PriceObservations
            .OrderByDescending(o => o.ObservedAt)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefault() is decimal latest && !PlaceholderPrice.IsBelowFloor(latest)
            ? latest
            : null;
}
