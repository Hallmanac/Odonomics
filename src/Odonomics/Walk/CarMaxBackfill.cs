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
/// in the first place. That comparison tolerates three known gaps between a page's own words and what
/// the ledger stamped from them: the ledger's model is the pair's canonical scenario model rather than
/// the page's own title text ("Camry Hybrid" against a page titled "2025 Toyota Camry"), so make
/// compares case-insensitively and the ledger's model also matches a page whose title is that same
/// model with a trailing " Hybrid" stripped (never the other way around, so a plain, non-hybrid
/// candidate is never matched to a page whose own title does say "Hybrid"); the ledger's mileage can
/// be overwritten to an exact figure by a later sighting from a different source, so it is rounded to
/// the nearest thousand the same way CarMax's own pages always are before comparing; and the posting's
/// price can move on from what the page said by the time this runs, so any price the posting has ever
/// been observed at counts, not only its latest. Two different
/// candidate postings that tie this way (a real, if rare, occurrence: two same-year, same-trim,
/// same-mileage cars of the same model) can't be told apart, so neither is touched; a candidate no
/// recorded page matches at all is left alone the same way. A candidate that ties two or more distinct
/// recorded pages is left alone for the identical reason — nothing on either side says which one is
/// really its own page.</para>
///
/// <para>When a posting matches more than one recorded page (the same car walked more than once, across
/// different runs), the page from the newest run wins, since a later recording can only be truer than
/// an older one about which store the car is at now — unless the pages that tie disagree about which
/// store that is, or two of them come from the very same run. Either is proof the pages are two
/// different cars that merely share a fingerprint (CarMax prices in round, model-year-and-mileage-keyed
/// steps, so this does happen among its own inventory), not one car recorded twice, since a single
/// run's own detail pages are never the same car under two different files; a tie like that is left
/// alone rather than trusted.</para>
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

        // A fingerprint any two CarMax postings both carry, candidate or not, can't be told apart from
        // a recorded page alone, so neither is touched: a posting a --revisit already resolved still
        // proves the same collision for a candidate that still shares its fingerprint.
        HashSet<CarMaxDetailFingerprint> ambiguousFingerprints =
        [
            .. allPostings
                .GroupBy(c => c.Fingerprint)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
        ];

        List<RecordedPage> recordedPages = ScanRecordings(dataDirectory);

        int filled = 0;
        int couldNotMatch = 0;

        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ambiguousFingerprints.Contains(candidate.Fingerprint)
                || !TryFindMatch(candidate, recordedPages, out RecordedMatch match))
            {
                // A posting already on a real store only ever entered this loop for its availability
                // reading, which a missing attribute usually just means is unremarkable ("not
                // reserved", cleared the same way a recognized store line clears it): counting it
                // alongside a bare posting that still has no store at all would overstate how many
                // postings this run actually failed to place.
                if (candidate.IsBareDealer)
                {
                    couldNotMatch++;
                }
                else
                {
                    alreadySet++;
                }

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

        List<decimal> observedPrices = [.. posting.PriceObservations.Select(o => o.Price).Distinct()];
        decimal? latestPrice = posting.PriceObservations
            .OrderByDescending(o => o.ObservedAt)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefault();

        // The latest price still identifies the candidate for the ambiguous-fingerprint check above
        // (two postings tying on it are exactly as untrustworthy whichever price either is quoted at
        // right now); TryFindMatch below reaches past it to every price this posting has ever carried.
        var fingerprint = new CarMaxDetailFingerprint(vehicle.Year, vehicle.Make, vehicle.Model, vehicle.Trim, vehicle.Mileage, latestPrice);
        return new Candidate(posting.Id, fingerprint, observedPrices, isBareDealer, hasAvailability);
    }

    /// <summary>Every recorded CarMax detail page under <paramref name="dataDirectory"/>'s
    /// <c>walks/carmax/&lt;run&gt;/&lt;pair&gt;/detail-N.txt</c> files, read once. Kept as a flat list
    /// rather than keyed by fingerprint: <see cref="TryFindMatch"/> compares a candidate against a
    /// page's fingerprint with tolerances a plain dictionary lookup can't express (see the class doc),
    /// and needs every page a candidate could plausibly match, not only the ones an exact fingerprint
    /// would have kept.</summary>
    private static List<RecordedPage> ScanRecordings(string dataDirectory)
    {
        var pages = new List<RecordedPage>();
        string carMaxRoot = Path.Combine(dataDirectory, "walks", "carmax");
        if (!Directory.Exists(carMaxRoot))
        {
            return pages;
        }

        foreach (string runDirectory in Directory.EnumerateDirectories(carMaxRoot))
        {
            string runFolder = Path.GetFileName(runDirectory);
            foreach (string pairDirectory in Directory.EnumerateDirectories(runDirectory))
            {
                foreach (string detailFile in Directory.EnumerateFiles(pairDirectory, "detail-*.txt"))
                {
                    string text = File.ReadAllText(detailFile);
                    if (CarMaxDetailFingerprints.Read(text) is not { } fingerprint)
                    {
                        continue;
                    }

                    pages.Add(new RecordedPage(fingerprint, runFolder, CarMaxStores.Read(text), CarMaxStores.ReadAvailability(text)));
                }
            }
        }

        return pages;
    }

    /// <summary>Every recorded page that could plausibly be <paramref name="candidate"/>'s own (see
    /// <see cref="Matches"/>), reduced to the one <paramref name="match"/> to trust, or false when
    /// there is none to trust: no page matched at all, the pages that did disagree about which store
    /// the car is at (proof they're two different cars, not one recorded twice), or two of them come
    /// from the very same run (proof of the same thing, since a run's own detail pages are never one
    /// car under two different files). Otherwise the page from the newest run wins; run folders sort
    /// newest last by name (<c>yyyyMMdd-HHmmss</c>), so an ordinal string comparison is exactly
    /// chronological order.</summary>
    private static bool TryFindMatch(Candidate candidate, List<RecordedPage> recordedPages, out RecordedMatch match)
    {
        List<RecordedPage> matches = [.. recordedPages.Where(page => Matches(candidate, page.Fingerprint))];
        if (matches.Count == 0)
        {
            match = default;
            return false;
        }

        bool sameRunTwice = matches.GroupBy(p => p.RunFolder).Any(g => g.Count() > 1);
        int distinctStores = matches
            .Select(p => p.Store)
            .OfType<ResolvedDealer>()
            .Select(store => (store.Name, store.Location))
            .Distinct()
            .Count();

        if (sameRunTwice || distinctStores > 1)
        {
            match = default;
            return false;
        }

        RecordedPage newest = matches.Aggregate((a, b) => string.CompareOrdinal(a.RunFolder, b.RunFolder) >= 0 ? a : b);
        match = new RecordedMatch(newest.Store, newest.Availability);
        return true;
    }

    /// <summary>Whether <paramref name="page"/> could be the page <paramref name="candidate"/> was
    /// walked from: same year, make (case-insensitively), and model (see <see cref="ModelsMatch"/>);
    /// the candidate's mileage rounded to the nearest thousand the way CarMax's own pages always are
    /// equals the page's; the trim matches case-insensitively; and the page's price, if it states one,
    /// is one the posting has been observed at some point, not necessarily its latest.</summary>
    private static bool Matches(Candidate candidate, CarMaxDetailFingerprint page)
    {
        CarMaxDetailFingerprint fingerprint = candidate.Fingerprint;
        return fingerprint.Year == page.Year
            && string.Equals(fingerprint.Make, page.Make, StringComparison.OrdinalIgnoreCase)
            && ModelsMatch(fingerprint.Model, page.Model)
            && string.Equals(fingerprint.Trim, page.Trim, StringComparison.OrdinalIgnoreCase)
            && CarMaxRoundedMileage(fingerprint.Mileage) == page.Mileage
            && (page.Price is not { } pagePrice || candidate.ObservedPrices.Contains(pagePrice));
    }

    /// <summary>Whether <paramref name="candidateModel"/> (the ledger's model, the pair's canonical
    /// scenario model, e.g. "Camry Hybrid") and <paramref name="pageModel"/> (the page's own title
    /// text, e.g. "Camry") name the same car: an exact, case-insensitive match, or the candidate's
    /// model with any trailing " Hybrid" stripped matching the page's model outright. Only the
    /// candidate side is ever stripped: a page whose own title does say "Hybrid" is unambiguous about
    /// being one, so it is never treated as a match for a plain, non-hybrid candidate just because
    /// they'd share a base model.</summary>
    private static bool ModelsMatch(string candidateModel, string pageModel) =>
        string.Equals(candidateModel, pageModel, StringComparison.OrdinalIgnoreCase)
        || string.Equals(BaseModel(candidateModel), pageModel, StringComparison.OrdinalIgnoreCase);

    private static string BaseModel(string model)
    {
        int hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return hybridIndex < 0 ? model : model[..hybridIndex];
    }

    /// <summary>CarMax rounds any mileage at or above 1,000 to the nearest thousand on its own pages
    /// (see <see cref="CarMaxDetailFingerprints"/>), so a ledger mileage a later sighting from a
    /// different, more exact source has since overwritten is rounded the same way before it's compared
    /// to one.</summary>
    private static int CarMaxRoundedMileage(int mileage) =>
        mileage < 1000
            ? mileage
            : (int)Math.Round(mileage / 1000.0, MidpointRounding.AwayFromZero) * 1000;

    private readonly record struct Candidate(int PostingId, CarMaxDetailFingerprint Fingerprint, IReadOnlyList<decimal> ObservedPrices, bool IsBareDealer, bool HasAvailability)
    {
        /// <summary>Whether this posting is missing anything a recorded page could fill: a real
        /// store (it is still on the bare "CarMax" fallback) or an availability reading. A posting
        /// with a real store and an availability reading already needed nothing to begin with. A
        /// posting that enters the loop only for the availability half and comes up with no match is
        /// still counted as already set rather than could-not-match: a missing attribute on a posting
        /// that already has a real store is the ordinary shape of "not reserved" (the same clear a
        /// recognized store line applies during an ordinary walk), not proof this run failed to place
        /// it, and counting it as a failure would overstate how many bare postings actually stayed
        /// bare.</summary>
        public bool NeedsMatch => IsBareDealer || !HasAvailability;
    }

    private readonly record struct RecordedPage(CarMaxDetailFingerprint Fingerprint, string RunFolder, ResolvedDealer? Store, string? Availability);

    private readonly record struct RecordedMatch(ResolvedDealer? Store, string? Availability);
}
