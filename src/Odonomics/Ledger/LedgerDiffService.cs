using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;

namespace Odonomics.Ledger;

public sealed record NewPostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal Price);

public sealed record MovedPostingEntry(string Vin, int Year, string Make, string Model, string Source, string OldUrl, string NewUrl);

public sealed record PriceDropEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal PreviousPrice, decimal CurrentPrice);

public sealed record GonePostingEntry(string Vin, int Year, string Make, string Model, string Source, string Url, decimal LastKnownPrice, string Reason);

/// <summary>The short reason a "gone" posting is reported, so a reader can tell a car that left the
/// market from one this run no longer looks for.</summary>
public static class GoneReasons
{
    public const string BelowYearFacet = "below year facet";
    public const string OverMileage = "over mileage";
    public const string SearchMoved = "search moved";
    public const string NotOnSearchPage = "not on search page";
}

public sealed record SearchDiff(
    IReadOnlyList<NewPostingEntry> New,
    IReadOnlyList<MovedPostingEntry> Moved,
    IReadOnlyList<PriceDropEntry> PriceDrops,
    IReadOnlyList<GonePostingEntry> Gone);

/// <summary>
/// Diffs one run against whatever ran before it, per source and model. A vehicle counts as "new"
/// because this run is the one that first created its ledger row (its FirstSeen matches the run's
/// own timestamp), or because this run created its first-ever posting on a source that VIN has
/// never been seen on before: a VIN the ledger already knew about through one source is still
/// "new" the first time a different source turns up a posting for it, since that's genuinely new
/// information about where the car is listed. What a VIN the ledger already knew about never gets
/// reported as "new" for is a fresh posting row on a source it was already known on (a relisted
/// URL, or, before URL canonicalization, a per-run query-string change); that's either "moved"
/// (the same source's posting reappeared under a different URL, price unchanged or higher) or
/// folded into "price-dropped" (same shape, but the new posting's price is actually lower than the
/// stale posting's last known price) rather than "new". A posting is "price-dropped" when this run
/// itself appended a lower price than the one before it on that same posting row, and "gone" when
/// the last run to cover that posting's own source and model before this one had it active but
/// this run never touched it. See LedgerUpsertService: every posting touched by a run is stamped
/// with that run's own StartedAt, not wall-clock time, which is what makes this an exact equality
/// comparison rather than an elapsed-time heuristic. Scoping "gone" to each posting's own source
/// and model (via <see cref="RunEntity.Sources"/>, one "source:model" token per pair the run
/// actually covered) keeps a search from reporting a walk's postings gone and vice versa, and
/// keeps a run that only covered one model from marking every other model on that same source as
/// checked too. Every "gone" entry carries a reason, checked in order: the vehicle's year is below
/// the scenario's minimum for its model, its mileage is over the scenario's maximum, the run that
/// last saw it searched a different zip or radius than this one, or otherwise it simply is not on
/// the search page any more. The first three are cars the search no longer asks for; only the last
/// is a car that has likely left the market. "Search moved" compares the zip and radius each run
/// recorded (see <see cref="RunEntity.Zip"/>), so a run recorded before the ledger kept them never
/// yields it.
/// </summary>
public sealed class LedgerDiffService(OdonomicsDbContext db)
{
    public async Task<SearchDiff> ComputeAsync(RunEntity currentRun, Scenario scenario, CancellationToken cancellationToken)
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
        var newSourceSightings = new HashSet<(string Vin, string Source)>();
        var movedSightings = new HashSet<(string Vin, string Source)>();
        var priceDropSightings = new HashSet<(string Vin, string Source)>();

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
                    // move (nothing on this source to have moved from), so it's reported under
                    // "New" alongside genuinely new vehicles, the same heading this posting would
                    // have landed under before New was keyed on the vehicle's own FirstSeen rather
                    // than the posting's. The vehicle itself isn't new, but this sighting is. Deduped
                    // by (VIN, source) rather than VIN alone: unlike the genuinely-new-vehicle branch
                    // above, where one entry per VIN is the right call, the same already-known VIN can
                    // legitimately turn up new on two different sources in the same run (a bare `odo
                    // walk` covers every walk site together), and each is its own new sighting.
                    if (newSourceSightings.Add((vehicle.Vin, posting.Source)))
                    {
                        newEntries.Add(new NewPostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, currentPrice));
                    }

                    continue;
                }

                decimal stalePrice = stale.PriceObservations.OrderByDescending(o => o.ObservedAt).First().Price;
                if (stalePrice > currentPrice)
                {
                    // Deduped by (VIN, source) against the same set the ordinary price-drop path
                    // below adds to: a VIN can carry two postings on one source in a single run (a
                    // relisted URL alongside an untouched older one), and without sharing the set
                    // each path would report its own drop for the same (VIN, source), disagreeing on
                    // "was" price. The same already-known VIN can still legitimately drop price on
                    // two different sources in one run, which this key still allows.
                    if (priceDropSightings.Add((vehicle.Vin, posting.Source)))
                    {
                        priceDrops.Add(new PriceDropEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, stalePrice, currentPrice));
                    }
                }
                else if (movedSightings.Add((vehicle.Vin, posting.Source)))
                {
                    moved.Add(new MovedPostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, stale.Url, posting.Url));
                }

                continue;
            }

            // A dropped price only belongs to this run when this run is the one that appended the
            // newer of the two observations; otherwise the price was already reported dropped on
            // whichever earlier run actually saw it change, and repeating it here would be stale
            // news every run after until the price moves again. Deduped by (VIN, source) against the
            // same set the relist path above adds to, the same reasoning as the branches above: the
            // same already-known VIN can drop price on two different sources' untouched-URL postings
            // in one run, and each is its own drop.
            if (observations.Count >= 2 && observations[^1].ObservedAt == currentRun.StartedAt && observations[^2].Price > currentPrice
                && priceDropSightings.Add((vehicle.Vin, posting.Source)))
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
                RunEntity? lastSeenRun = priorRuns.FirstOrDefault(r => r.StartedAt == previousCoverage);
                gone.Add(new GonePostingEntry(vehicle.Vin, vehicle.Year, vehicle.Make, vehicle.Model, posting.Source, posting.Url, lastKnownPrice, GoneReason(vehicle, lastSeenRun, currentRun, scenario)));
            }
        }

        return new SearchDiff(newEntries, moved, priceDrops, gone);
    }

    private static string GoneReason(VehicleEntity vehicle, RunEntity? lastSeenRun, RunEntity currentRun, Scenario scenario)
    {
        if (vehicle.Year < scenario.Filters.MinYearFor($"{vehicle.Make} {vehicle.Model}"))
        {
            return GoneReasons.BelowYearFacet;
        }

        if (vehicle.Mileage > scenario.Filters.MaxMileage)
        {
            return GoneReasons.OverMileage;
        }

        return SearchAreaChanged(lastSeenRun, currentRun)
            ? GoneReasons.SearchMoved
            : GoneReasons.NotOnSearchPage;
    }

    private static bool SearchAreaChanged(RunEntity? lastSeenRun, RunEntity currentRun) =>
        lastSeenRun is not null
        && ((lastSeenRun.Zip is not null && lastSeenRun.Zip != currentRun.Zip)
            || (lastSeenRun.RadiusMiles is not null && lastSeenRun.RadiusMiles != currentRun.RadiusMiles));
}
