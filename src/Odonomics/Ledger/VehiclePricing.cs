using Odonomics.Domain;

namespace Odonomics.Ledger;

public static class VehiclePricing
{
    /// <summary>The lowest latest price among this vehicle's postings that were still active as of
    /// the most recent full coverage of that posting's own source and model (LastSeen is that run's
    /// timestamp or later; see LedgerUpsertService and <see cref="RunSources.LatestCoverageBySource"/>).
    /// A posting whose source/model no run has ever covered, or that the latest full coverage saw, still
    /// counts, and so does one a later capped walk reached or never reached, or one a run's render wait
    /// gave up on while the ledger already held it (<see cref="PostingEntity.CardUnrenderedSeenAt"/> at or
    /// after the latest full coverage); only a posting whose source/model was fully checked more recently
    /// without seeing it again, or without its card failing to render again, drops out.
    /// This is the asking price alone, which is what the price red flags compare; the cost model
    /// uses <see cref="LowestCurrentPurchasePrice"/>.</summary>
    public static decimal? LowestCurrentPrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        decimal?[] known = [.. ActivePostings(vehicle, latestCoverageBySource).Select(LatestAskingPrice).Where(p => p is not null)];
        return known.Length == 0 ? null : known.Min();
    }

    /// <summary>The cheapest way to take this vehicle home among its active, purchasable postings (see
    /// <see cref="LowestCurrentPrice"/> for which are active; a posting currently reserved or in transit,
    /// see <see cref="IsReservedOrInTransit"/>, is never a candidate here even when it is the cheapest
    /// priced one, since it cannot actually be bought right now): each posting's latest asking price
    /// plus the fee for <paramref name="fulfillment"/>, plus the fees it itemized on top of that price
    /// when its fee posture is itemized, so a far-away carvana car, or a dealer whose fees are added at
    /// the desk, is compared honestly with one that has neither. Under delivery the fee is the shipping
    /// fee; under pickup it is the pickup fee, or the shipping fee when no pickup fee was read (see
    /// <see cref="PurchasePrice"/>). A posting with a null fee costs its asking price, and so does one
    /// whose posture is all-in (its price already holds its fees, and any shipping fee it shows), unknown (the page did not say), or
    /// never read. When two postings cost the same to take home, the one with the lower asking price is
    /// reported.</summary>
    public static PurchasePrice? LowestCurrentPurchasePrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment)?.Price;

    /// <summary>The active, purchasable posting <see cref="LowestCurrentPurchasePrice"/> reports the price
    /// of, so a display that sits beside that price (a site's own badge for the listing, its dealer and fee
    /// posture) speaks for the same listing; null when no active, purchasable posting has a price.</summary>
    public static PostingEntity? LowestCurrentPurchasePosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment)?.Posting;

    private static (PostingEntity Posting, PurchasePrice Price)? CheapestPosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment)
    {
        (PostingEntity Posting, PurchasePrice Price)[] known =
        [
            .. ActivePostings(vehicle, latestCoverageBySource)
                .Where(p => !IsReservedOrInTransit(p))
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
    /// display, as the fees an all-in price includes. The same goes for a shipping fee on an all-in posting:
    /// the only card that says so is one that says "Price includes $462 shipping", so the fee is carried for
    /// display and never added (see <see cref="PurchasePrice.ShippingIncluded"/>).</summary>
    private static PurchasePrice PurchasePriceOf(PostingEntity posting, decimal asking, Fulfillment fulfillment) =>
        posting.FeePosture switch
        {
            FeePostures.Itemized => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, posting.ItemizedFeesTotal, posting.FeePosture),
            FeePostures.AllIn => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture, posting.ItemizedFeesTotal, ShippingIncluded: posting.ShippingFee is not null),
            _ => new(asking, posting.ShippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture),
        };

    /// <summary>True when this vehicle has at least one active posting (see <see cref="LowestCurrentPrice"/>
    /// for which those are) currently carrying CarMax's own <see cref="PostingAttributeNames.Availability"/>
    /// attribute ("Reserved for another buyer" or "In transit, not yet purchasable"), and none of its active
    /// postings is both purchasable (not carrying that attribute) and priced: whether because every active
    /// posting carries it, or because the only ones that don't have no valid price to rank or budget on (see
    /// <see cref="LatestAskingPrice"/>) and so are indistinguishable from gone. Either way <see cref="CheapestPosting"/>
    /// has nothing to price the vehicle from, so this is the reason to give instead of ranking it on the
    /// reserved posting's own price. A vehicle with no active posting at all (every posting gone) is false
    /// here too, since that is <see cref="Domain.Scorer.FilterReasons"/>'s own separate reason to give, not
    /// this one's.</summary>
    public static bool OnlyReservedOrInTransit(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        PostingEntity[] active = [.. ActivePostings(vehicle, latestCoverageBySource)];
        return active.Length > 0
            && active.Any(IsReservedOrInTransit)
            && !active.Any(p => !IsReservedOrInTransit(p) && LatestAskingPrice(p) is not null);
    }

    /// <summary>The <see cref="PostingAttributeNames.Availability"/> note of the first active posting that
    /// carries one, even when a different, purchasable posting is the one <see cref="LowestCurrentPurchasePosting"/>
    /// actually prices the vehicle from: informational only, for `odo rank` to show beside a row that still
    /// ranks normally off another posting. <see cref="OnlyReservedOrInTransit"/> is what actually excludes a
    /// vehicle; null when no active posting carries the attribute.</summary>
    public static string? ReservedOrInTransitNote(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        ActivePostings(vehicle, latestCoverageBySource)
            .SelectMany(p => p.Attributes)
            .FirstOrDefault(a => a.Name == PostingAttributeNames.Availability)?.Value;

    /// <summary>True when the posting currently carries CarMax's <see cref="PostingAttributeNames.Availability"/>
    /// attribute at all: the attribute is only ever set to say a car can't be bought right now (see its own
    /// doc comment), and a run whose detail visit finds the reservation lifted removes the row entirely
    /// rather than changing its value (see <see cref="LedgerUpsertService.ApplyAttributes"/> and
    /// <see cref="PostingEntity.AvailabilityClearedAt"/>), so the row's mere presence is enough.</summary>
    private static bool IsReservedOrInTransit(PostingEntity posting) =>
        posting.Attributes.Any(a => a.Name == PostingAttributeNames.Availability);

    private static IEnumerable<PostingEntity> ActivePostings(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        vehicle.Postings.Where(p =>
        {
            string key = RunSources.Key(p.Source, vehicle.Model);
            return !latestCoverageBySource.TryGetValue(key, out DateTimeOffset latestCoverage)
                || p.LastSeen >= latestCoverage
                || p.CardUnrenderedSeenAt >= latestCoverage;
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
