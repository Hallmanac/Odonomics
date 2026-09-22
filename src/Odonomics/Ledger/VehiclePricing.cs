namespace Odonomics.Ledger;

public static class VehiclePricing
{
    /// <summary>The lowest latest price among this vehicle's postings that were still active as of
    /// the most recent run that covered that posting's own source and model (LastSeen equals that
    /// run's own timestamp; see LedgerUpsertService). A posting whose source/model no run has ever
    /// covered, or whose most recent covering run was also the one that saw it, still counts; only
    /// a posting whose source/model was checked more recently without seeing it again drops out.</summary>
    public static decimal? LowestCurrentPrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        IEnumerable<PostingEntity> activePostings = vehicle.Postings.Where(p =>
        {
            string key = RunSources.Key(p.Source, vehicle.Model);
            return !latestCoverageBySource.TryGetValue(key, out DateTimeOffset latestCoverage) || p.LastSeen == latestCoverage;
        });

        decimal?[] latestPrices =
        [
            .. activePostings.Select(p => p.PriceObservations
                .OrderByDescending(o => o.ObservedAt)
                .Select(o => (decimal?)o.Price)
                .FirstOrDefault()),
        ];

        decimal?[] known = [.. latestPrices.Where(p => p is not null)];
        return known.Length == 0 ? null : known.Min();
    }
}
