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
    /// plus its shipping fee when one is known, so a far-away carvana car is compared honestly with
    /// one that has no fee. A posting with a null fee costs its asking price, as before. When two
    /// postings cost the same to take home, the one with the lower asking price is reported.</summary>
    public static PurchasePrice? LowestCurrentPurchasePrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        PurchasePrice[] known =
        [
            .. ActivePostings(vehicle, latestCoverageBySource)
                .Select<PostingEntity, PurchasePrice?>(p => LatestAskingPrice(p) is decimal asking ? new PurchasePrice(asking, p.ShippingFee) : null)
                .OfType<PurchasePrice>(),
        ];

        return known.Length == 0
            ? null
            : known.OrderBy(p => p.Total).ThenBy(p => p.Asking).First();
    }

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
