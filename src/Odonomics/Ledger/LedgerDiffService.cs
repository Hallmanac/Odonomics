using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

public sealed record NewPostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal Price);

public sealed record MovedPostingEntry(string Vin, int Year, string Make, string Model, string Source, string OldUrl, string NewUrl);

public sealed record PriceDropEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal PreviousPrice, decimal CurrentPrice);

public sealed record GonePostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal LastKnownPrice);

public sealed record SearchDiff(
    IReadOnlyList<NewPostingEntry> New,
    IReadOnlyList<MovedPostingEntry> Moved,
    IReadOnlyList<PriceDropEntry> PriceDrops,
    IReadOnlyList<GonePostingEntry> Gone);

/// <summary>
/// Diffs one run against whatever ran before it, per source and model. A vehicle counts as "new"
/// only when this run is the one that first created its ledger row (its FirstSeen matches the
/// run's own timestamp), never merely because one of its postings did: a VIN the ledger already
/// knew about is never "new" again just because it turned up under a fresh posting row (a relisted
/// URL, or, before URL canonicalization, a per-run query-string change). When this run creates a
/// brand-new posting row for an already-known vehicle, that's either "moved" (the same source's
/// posting reappeared under a different URL, price unchanged or higher) or folded into
/// "price-dropped" (same shape, but the new posting's price is actually lower than the stale
/// posting's last known price) rather than "new". A posting is "price-dropped" when this run
/// itself appended a lower price than the one before it on that same posting row, and "gone" when
/// the last run to cover that posting's own source and model before this one had it active but
/// this run never touched it. See LedgerUpsertService: every posting touched by a run is stamped
/// with that run's own StartedAt, not wall-clock time, which is what makes this an exact equality
/// comparison rather than an elapsed-time heuristic. Scoping "gone" to each posting's own source
/// and model (via <see cref="RunEntity.Sources"/>, one "source:model" token per pair the run
/// actually covered) keeps a search from reporting a walk's postings gone and vice versa, and
/// keeps a run that only covered one model from marking every other model on that same source as
/// checked too.
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
        var moved = new List<MovedPostingEntry>();
        var priceDrops = new List<PriceDropEntry>();
        var newEntryVins = new HashSet<string>();
        var movedVins = new HashSet<string>();
        var priceDropVins = new HashSet<string>();

        // A brand-new posting row for a vehicle the ledger already knew about might be the same
        // listing back under a changed URL; the only way to tell is to find whatever stale posting
        // (untouched this run) shared that vehicle's VIN and source before this run. Fetched once
        // up front, for every such candidate at once, rather than one query per posting.
        List<string> vinsNeedingStaleLookup = [.. touchedThisRun
            .Where(p => p.FirstSeen == currentRun.StartedAt && p.Vehicle!.FirstSeen != currentRun.StartedAt)
            .Select(p => p.VehicleVin)
            .Distinct()];
        Dictionary<string, List<PostingEntity>> stalePostingsByVin = vinsNeedingStaleLookup.Count == 0
            ? []
            : (await db.Postings
                .Include(p => p.PriceObservations)
                .Where(p => vinsNeedingStaleLookup.Contains(p.VehicleVin) && p.LastSeen != currentRun.StartedAt)
                .ToListAsync(cancellationToken))
                .GroupBy(p => p.VehicleVin)
                .ToDictionary(g => g.Key, g => g.ToList());

        // Ordered by Id (insertion order) so that when a VIN reached the ledger through two
        // postings touched in the same run (two detail links, two dealers cross-listing the same
        // car), the entry that survives dedup below is the one this run saw first, not whichever
        // the query happened to return last.
        foreach (PostingEntity posting in touchedThisRun.OrderBy(p => p.Id))
        {
            VehicleEntity vehicle = posting.Vehicle ?? throw new InvalidOperationException($"posting {posting.Id} has no vehicle");
            List<PriceObservationEntity> observations = [.. posting.PriceObservations.OrderBy(o => o.ObservedAt)];
            decimal currentPrice = observations[^1].Price;

            if (vehicle.FirstSeen == currentRun.StartedAt)
            {
                if (newEntryVins.Add(vehicle.Vin))
                {
                    newEntries.Add(new NewPostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, currentPrice));
                }

                continue;
            }

            if (posting.FirstSeen == currentRun.StartedAt)
            {
                PostingEntity? stale = stalePostingsByVin.TryGetValue(vehicle.Vin, out List<PostingEntity>? candidates)
                    ? candidates.Where(c => c.Source == posting.Source).OrderByDescending(c => c.LastSeen).FirstOrDefault()
                    : null;
                if (stale is null)
                {
                    // First time this already-known VIN has ever been seen on this source: not a
                    // move (nothing on this source to have moved from) and not "new" (the vehicle
                    // itself isn't), so this posting isn't reported under any heading.
                    continue;
                }

                decimal stalePrice = stale.PriceObservations.OrderByDescending(o => o.ObservedAt).First().Price;
                if (stalePrice > currentPrice)
                {
                    if (priceDropVins.Add(vehicle.Vin))
                    {
                        priceDrops.Add(new PriceDropEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, stalePrice, currentPrice));
                    }
                }
                else if (movedVins.Add(vehicle.Vin))
                {
                    moved.Add(new MovedPostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, stale.Url, posting.Url));
                }

                continue;
            }

            // A dropped price only belongs to this run when this run is the one that appended the
            // newer of the two observations; otherwise the price was already reported dropped on
            // whichever earlier run actually saw it change, and repeating it here would be stale
            // news every run after until the price moves again.
            if (observations.Count >= 2 && observations[^1].ObservedAt == currentRun.StartedAt && observations[^2].Price > currentPrice
                && priceDropVins.Add(vehicle.Vin))
            {
                priceDrops.Add(new PriceDropEntry(
                    vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url,
                    observations[^2].Price, currentPrice));
            }
        }

        // A VIN sighted anywhere this run, under any posting, is never "gone" even if the specific
        // posting that used to carry it went untouched: a relisted URL, a second dealer's listing,
        // or (before URL canonicalization) a per-run query-string change all leave the old posting
        // stale while the vehicle itself is still on the market. See the "New" and "Moved" entries
        // above for where that fresh sighting itself gets reported, if it gets reported at all.
        var vinsSightedThisRun = new HashSet<string>(touchedThisRun.Select(p => p.VehicleVin));

        string[] tokens = RunSources.Split(currentRun);
        List<RunEntity> allRuns = await db.Runs.ToListAsync(cancellationToken);
        List<RunEntity> priorRuns = [.. allRuns.Where(r => r.Id != currentRun.Id && r.StartedAt < currentRun.StartedAt)];
        Dictionary<string, DateTimeOffset> previousCoverageBySource = RunSources.LatestCoverageBySource(priorRuns);

        var gone = new List<GonePostingEntry>();
        var goneVins = new HashSet<string>();
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
                .OrderBy(p => p.Id)
                .ToListAsync(cancellationToken);

            foreach (PostingEntity posting in stillMarkedFromPreviousCoverage)
            {
                VehicleEntity vehicle = posting.Vehicle ?? throw new InvalidOperationException($"posting {posting.Id} has no vehicle");
                if (vinsSightedThisRun.Contains(vehicle.Vin) || !goneVins.Add(vehicle.Vin))
                {
                    continue;
                }

                decimal lastKnownPrice = posting.PriceObservations.OrderByDescending(o => o.ObservedAt).First().Price;
                gone.Add(new GonePostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, lastKnownPrice));
            }
        }

        return new SearchDiff(newEntries, moved, priceDrops, gone);
    }
}
