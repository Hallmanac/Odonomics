using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

public sealed record NewPostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal Price);

public sealed record PriceDropEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal PreviousPrice, decimal CurrentPrice);

public sealed record GonePostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal LastKnownPrice);

public sealed record SearchDiff(
    IReadOnlyList<NewPostingEntry> New,
    IReadOnlyList<PriceDropEntry> PriceDrops,
    IReadOnlyList<GonePostingEntry> Gone);

/// <summary>
/// Diffs one run against the run before it. A posting counts as "new" when this run created it
/// (its FirstSeen matches the run's own timestamp), "price-dropped" when this run appended a
/// lower price than the one before it, and "gone" when the previous run had it active but this
/// run never touched it (its LastSeen still points at the previous run's timestamp). See
/// LedgerUpsertService: every posting touched by a run is stamped with that run's own
/// StartedAt, not wall-clock time, which is what makes this an exact equality comparison rather
/// than an elapsed-time heuristic.
/// </summary>
public sealed class LedgerDiffService(OdonomicsDbContext db)
{
    public async Task<SearchDiff> ComputeAsync(RunEntity currentRun, RunEntity? previousRun, CancellationToken cancellationToken)
    {
        List<PostingEntity> touchedThisRun = await db.Postings
            .Include(p => p.Vehicle)
            .Include(p => p.PriceObservations)
            .Where(p => p.LastSeen == currentRun.StartedAt)
            .ToListAsync(cancellationToken);

        var newEntries = new List<NewPostingEntry>();
        var priceDrops = new List<PriceDropEntry>();

        foreach (PostingEntity posting in touchedThisRun)
        {
            VehicleEntity vehicle = posting.Vehicle ?? throw new InvalidOperationException($"posting {posting.Id} has no vehicle");
            List<PriceObservationEntity> observations = [.. posting.PriceObservations.OrderBy(o => o.ObservedAt)];
            decimal currentPrice = observations[^1].Price;

            if (posting.FirstSeen == currentRun.StartedAt)
            {
                newEntries.Add(new NewPostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, currentPrice));
                continue;
            }

            if (observations.Count >= 2 && observations[^2].Price > currentPrice)
            {
                priceDrops.Add(new PriceDropEntry(
                    vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url,
                    observations[^2].Price, currentPrice));
            }
        }

        var gone = new List<GonePostingEntry>();
        if (previousRun is not null)
        {
            List<PostingEntity> stillMarkedFromPreviousRun = await db.Postings
                .Include(p => p.Vehicle)
                .Include(p => p.PriceObservations)
                .Where(p => p.LastSeen == previousRun.StartedAt)
                .ToListAsync(cancellationToken);

            foreach (PostingEntity posting in stillMarkedFromPreviousRun)
            {
                VehicleEntity vehicle = posting.Vehicle ?? throw new InvalidOperationException($"posting {posting.Id} has no vehicle");
                decimal lastKnownPrice = posting.PriceObservations.OrderByDescending(o => o.ObservedAt).First().Price;
                gone.Add(new GonePostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, lastKnownPrice));
            }
        }

        return new SearchDiff(newEntries, priceDrops, gone);
    }
}
