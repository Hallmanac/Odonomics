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
/// Diffs one run against whatever ran before it, per source and model. A posting counts as "new"
/// when this run created it (its FirstSeen matches the run's own timestamp), "price-dropped" when
/// this run itself appended a lower price than the one before it, and "gone" when the last run to
/// cover that posting's own source and model before this one had it active but this run never
/// touched it. See LedgerUpsertService: every posting touched by a run is stamped with that run's
/// own StartedAt, not wall-clock time, which is what makes this an exact equality comparison
/// rather than an elapsed-time heuristic. Scoping "gone" to each posting's own source and model
/// (via <see cref="RunEntity.Sources"/>, one "source:model" token per pair the run actually
/// covered) keeps a search from reporting a walk's postings gone and vice versa, and keeps a run
/// that only covered one model from marking every other model on that same source as checked too.
/// </summary>
public sealed class LedgerDiffService(OdonomicsDbContext db)
{
    public async Task<SearchDiff> ComputeAsync(RunEntity currentRun, CancellationToken cancellationToken)
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

            // A dropped price only belongs to this run when this run is the one that appended the
            // newer of the two observations; otherwise the price was already reported dropped on
            // whichever earlier run actually saw it change, and repeating it here would be stale
            // news every run after until the price moves again.
            if (observations.Count >= 2 && observations[^1].ObservedAt == currentRun.StartedAt && observations[^2].Price > currentPrice)
            {
                priceDrops.Add(new PriceDropEntry(
                    vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url,
                    observations[^2].Price, currentPrice));
            }
        }

        string[] tokens = RunSources.Split(currentRun);
        List<RunEntity> allRuns = await db.Runs.ToListAsync(cancellationToken);
        List<RunEntity> priorRuns = [.. allRuns.Where(r => r.Id != currentRun.Id && r.StartedAt < currentRun.StartedAt)];
        Dictionary<string, DateTimeOffset> previousCoverageBySource = RunSources.LatestCoverageBySource(priorRuns);

        var gone = new List<GonePostingEntry>();
        foreach (string token in tokens)
        {
            if (!previousCoverageBySource.TryGetValue(token, out DateTimeOffset previousCoverage))
            {
                continue; // this run is the first to ever cover this source/model; nothing to compare against
            }

            (string source, string model) = RunSources.SplitKey(token);
            List<PostingEntity> stillMarkedFromPreviousCoverage = await db.Postings
                .Include(p => p.Vehicle)
                .Include(p => p.PriceObservations)
                .Where(p => p.Source == source && p.Vehicle!.Model == model && p.LastSeen == previousCoverage)
                .ToListAsync(cancellationToken);

            foreach (PostingEntity posting in stillMarkedFromPreviousCoverage)
            {
                VehicleEntity vehicle = posting.Vehicle ?? throw new InvalidOperationException($"posting {posting.Id} has no vehicle");
                decimal lastKnownPrice = posting.PriceObservations.OrderByDescending(o => o.ObservedAt).First().Price;
                gone.Add(new GonePostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, lastKnownPrice));
            }
        }

        return new SearchDiff(newEntries, priceDrops, gone);
    }
}
