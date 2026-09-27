using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>How many CarMax postings a backfill run filled, found already set, and could not match
/// to any recorded page.</summary>
public sealed record CarMaxBackfillTally(int Filled, int AlreadySet, int CouldNotMatch);

/// <summary>Fills a CarMax posting's store and reserved-or-in-transit availability from a detail page
/// <c>odo walk carmax</c> has already recorded on disk, with no browser and no page visit (see
/// <c>CarMaxBackfillCommand</c>, wired up as <c>odo walk --backfill-carmax</c>).
///
/// <para>A posting is a candidate for this at all when its dealer is still the bare "CarMax" fallback
/// (<see cref="WalkSites.CarMaxDealerName"/> with no location, meaning no recorded page's store line
/// was ever read for it) or when it carries no <see cref="PostingAttributeNames.Availability"/>
/// attribute yet (meaning either the same thing, or that it was walked before that attribute existed
/// at all). Neither gap can be closed by opening the posting's own detail page again cheaply: CarMax's
/// nationwide search has to be re-walked in full to find one URL again, and only about one in four
/// postings actually needs this. What is cheap is reading the pages the walk already recorded under
/// the data directory's <c>walks/carmax/&lt;run&gt;/&lt;pair&gt;/detail-N.txt</c>, and reusing
/// <see cref="CarMaxStores.Read"/> and <see cref="CarMaxStores.ReadAvailability"/> (the same parsing
/// <c>WalkSite.DetailDealerReader</c> and <c>WalkSite.DetailAvailabilityReader</c> already use, not a
/// second copy of it) on whichever recorded page turns out to be that same car's.</para>
///
/// <para>The hard part is telling which recorded page is which posting's: a CarMax detail page's
/// happy path (the VIN read off its HTML succeeds) never keeps that HTML on disk, only the page's
/// plain visible text, which never states the VIN or the page's own URL. So a recorded page and a
/// posting can only be matched on what they both say about the car itself — its year, make, model,
/// trim, mileage, and asking price (see <see cref="CarMaxDetailFingerprint"/>) — since every one of
/// those figures is exactly what the original walk read off the very same page to store the posting
/// in the first place. Two different candidate postings that happen to share every one of those
/// figures (a real, if rare, occurrence: two same-year, same-trim, same-mileage, same-price cars of
/// the same model) can't be told apart this way, so neither is touched; a candidate no recorded
/// page's fingerprint matches at all is left alone the same way. When a posting's fingerprint matches
/// more than one recorded page (the same car walked more than once, across different runs), the page
/// from the newest run wins, since a later recording can only be truer than an older one about which
/// store the car is at now.</para>
///
/// <para>Nothing here is ever deleted, and a posting whose dealer is already a real store and whose
/// availability is already set is never even considered: this only ever fills a gap, never
/// reconciles one.</para></summary>
public static class CarMaxBackfill
{
    public static async Task<CarMaxBackfillTally> RunAsync(
        string dataDirectory,
        IReadOnlyList<PostingEntity> carMaxPostings,
        LedgerUpsertService upsertService,
        RunEntity run,
        CancellationToken cancellationToken)
    {
        List<Candidate> allPostings = [.. carMaxPostings.Select(ToCandidate).Where(c => c is not null).Select(c => c!.Value)];

        // A posting whose dealer is already a real store and whose availability is already read
        // needed nothing from this run to begin with; it never enters the matching below at all.
        int alreadySet = allPostings.Count(c => !c.NeedsMatch);
        List<Candidate> candidates = [.. allPostings.Where(c => c.NeedsMatch)];

        // A fingerprint two different candidate postings both carry can't be told apart from a
        // recorded page alone, so neither is touched.
        HashSet<CarMaxDetailFingerprint> ambiguousFingerprints =
        [
            .. candidates
                .GroupBy(c => c.Fingerprint)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
        ];

        Dictionary<CarMaxDetailFingerprint, RecordedMatch> recordings =
            ScanRecordings(dataDirectory, [.. candidates.Select(c => c.Fingerprint)]);

        int filled = 0;
        int couldNotMatch = 0;

        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ambiguousFingerprints.Contains(candidate.Fingerprint)
                || !recordings.TryGetValue(candidate.Fingerprint, out RecordedMatch match))
            {
                couldNotMatch++;
                continue;
            }

            bool wroteDealer = false;
            if (candidate.IsBareDealer)
            {
                if (match.Store is not { Name: { } storeName } store)
                {
                    // The one thing this posting needed, a recognizable store line, is exactly the
                    // one thing the matched page still doesn't carry.
                    couldNotMatch++;
                    continue;
                }

                await upsertService.SetPostingDealerAsync(candidate.PostingId, storeName, store.Location, cancellationToken);
                wroteDealer = true;
            }

            bool wroteAvailability = false;
            if (!candidate.HasAvailability && match.Availability is string availability)
            {
                await upsertService.SetPostingAttributesAsync(
                    candidate.PostingId,
                    new Dictionary<string, string> { [PostingAttributeNames.Availability] = availability },
                    [],
                    run,
                    cancellationToken);
                wroteAvailability = true;
            }

            if (wroteDealer || wroteAvailability)
            {
                filled++;
            }
            else
            {
                alreadySet++;
            }
        }

        return new CarMaxBackfillTally(filled, alreadySet, couldNotMatch);
    }

    private static Candidate? ToCandidate(PostingEntity posting)
    {
        VehicleEntity? vehicle = posting.Vehicle;
        if (vehicle is null)
        {
            return null;
        }

        bool isBareDealer = posting.Dealer is null
            || (posting.Dealer.NormalizedName == DealerNormalizer.Normalize(WalkSites.CarMaxDealerName)
                && string.IsNullOrEmpty(posting.Dealer.Location));
        bool hasAvailability = posting.Attributes.Any(a => a.Name == PostingAttributeNames.Availability);

        decimal? latestPrice = posting.PriceObservations
            .OrderByDescending(o => o.ObservedAt)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefault();

        var fingerprint = new CarMaxDetailFingerprint(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Trim, vehicle.Mileage, latestPrice);
        return new Candidate(posting.Id, fingerprint, isBareDealer, hasAvailability);
    }

    /// <summary>Every recorded CarMax detail page under <paramref name="dataDirectory"/>'s
    /// <c>walks/carmax/&lt;run&gt;/&lt;pair&gt;/detail-N.txt</c> files, read once, keyed by the
    /// fingerprint of whichever ones are worth keeping (see <paramref name="needed"/>): the newest
    /// run's page wins whenever more than one matches the same fingerprint. Run folders sort newest
    /// last by name (<c>yyyyMMdd-HHmmss</c>), so an ordinal string comparison is exactly chronological
    /// order.</summary>
    private static Dictionary<CarMaxDetailFingerprint, RecordedMatch> ScanRecordings(
        string dataDirectory, HashSet<CarMaxDetailFingerprint> needed)
    {
        var best = new Dictionary<CarMaxDetailFingerprint, (string RunFolder, RecordedMatch Match)>();
        string carMaxRoot = Path.Combine(dataDirectory, "walks", "carmax");
        if (!Directory.Exists(carMaxRoot))
        {
            return [];
        }

        foreach (string runDirectory in Directory.EnumerateDirectories(carMaxRoot))
        {
            string runFolder = Path.GetFileName(runDirectory);
            foreach (string pairDirectory in Directory.EnumerateDirectories(runDirectory))
            {
                foreach (string detailFile in Directory.EnumerateFiles(pairDirectory, "detail-*.txt"))
                {
                    string text = File.ReadAllText(detailFile);
                    if (CarMaxDetailFingerprints.Read(text) is not { } fingerprint || !needed.Contains(fingerprint))
                    {
                        continue;
                    }

                    if (best.TryGetValue(fingerprint, out (string RunFolder, RecordedMatch Match) existing)
                        && string.CompareOrdinal(existing.RunFolder, runFolder) >= 0)
                    {
                        continue;
                    }

                    best[fingerprint] = (runFolder, new RecordedMatch(CarMaxStores.Read(text), CarMaxStores.ReadAvailability(text)));
                }
            }
        }

        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Match);
    }

    private readonly record struct Candidate(int PostingId, CarMaxDetailFingerprint Fingerprint, bool IsBareDealer, bool HasAvailability)
    {
        /// <summary>Whether this posting is missing anything a recorded page could fill: a real
        /// store (it is still on the bare "CarMax" fallback) or an availability reading. A posting
        /// with a real store and an availability reading already needed nothing to begin with.</summary>
        public bool NeedsMatch => IsBareDealer || !HasAvailability;
    }

    private readonly record struct RecordedMatch(ResolvedDealer? Store, string? Availability);
}
