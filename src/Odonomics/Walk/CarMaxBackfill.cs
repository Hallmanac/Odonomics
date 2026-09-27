using System.Text.Json;
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
/// <para>Every posting's own detail page URL is on disk, even though the detail page's own visible text
/// never states it: the same <c>&lt;pair&gt;</c> folder that holds a run's <c>detail-N.txt</c> files also
/// holds that run's <c>cards*.json</c> (<see cref="SearchPageLinks.CardsJson"/>), one entry per search
/// result with its href (the posting's own canonical URL) and its own card text (the same year, make,
/// model, trim, mileage, and price the card showed before the walk ever opened the link). So a candidate
/// whose posting URL turns up as an href in some pair folder's <c>cards*.json</c> is anchored to that
/// folder's own detail page whose fingerprint (<see cref="CarMaxDetailFingerprint"/>) matches the card's:
/// the two were read from the very same walk, so nothing about either has had a chance to drift yet. That
/// anchor is trusted ahead of anything below, and is never subject to the fingerprint tolerances or
/// cross-posting ambiguity checks below, since a posting's own URL can only ever be its own page.</para>
///
/// <para>A candidate whose URL never turns up in any recorded <c>cards*.json</c> (the search that found
/// it ran before recording carried card text, or its card was never re-walked) falls back to matching by
/// fingerprint alone: the year, make, model, trim, mileage, and asking price a page and a posting both
/// state about the car (see <see cref="CarMaxDetailFingerprint"/>), since every one of those figures is
/// exactly what the original walk read off the very same page to store the posting in the first place.
/// That comparison tolerates three known gaps between a page's own words and what the ledger stamped from
/// them: the ledger's model is the pair's canonical scenario model rather than the page's own title text
/// ("Camry Hybrid" against a page titled "2025 Toyota Camry"), so make compares case-insensitively and
/// the ledger's model also matches a page whose title is that same model with a trailing " Hybrid"
/// stripped (never the other way around, so a plain, non-hybrid candidate is never matched to a page
/// whose own title does say "Hybrid"); the ledger's mileage can be overwritten to an exact figure by a
/// later sighting from a different source, so it is rounded to the nearest thousand the same way CarMax's
/// own pages always are before comparing; and the posting's price can move on from what the page said by
/// the time this runs, so any price the posting has ever been observed at counts, not only its latest.
/// Two different candidate postings that tie this way (a real, if rare, occurrence: two same-year,
/// same-trim, same-mileage cars of the same model) can't be told apart, so neither is touched; a
/// candidate no recorded page matches at all is left alone the same way. A candidate that ties two or
/// more distinct recorded pages is left alone for the identical reason: nothing on either side says which
/// one is really its own page. The same is true the other way around: a single recorded page that
/// tolerantly matches more than one posting (candidate or already resolved) can never be trusted as the
/// page a candidate is filled from, since those tolerances can tie two postings whose own fingerprints no
/// longer match exactly (one posting's mileage or price has drifted since the page was recorded, the
/// other's hasn't) to the very same page. That page still counts against every candidate it tolerantly
/// matches, though, rather than being dropped from view outright: a candidate is left alone whenever any
/// recorded page it tolerantly matches, trustworthy enough to fill from or not, disagrees about the
/// store or the availability with the page that would otherwise be trusted. Dropping an untrustworthy
/// page outright, instead of only barring it from being the source, would let a bare posting be filled
/// from an older recording while a newer page that also plausibly is its own car, and disagrees about
/// which store it's at now, went unseen.</para>
///
/// <para>When a candidate (anchored by URL or matched by fingerprint) ties more than one trustworthy
/// recorded page (the same car walked more than once, across different runs), the page from the newest
/// run wins, since a later recording can only be truer than an older one about which store the car is at
/// now, unless the pages that tie disagree about which store or availability that is, or two of them come
/// from the very same run. Any of those is proof the pages are two different cars that merely share a
/// fingerprint (CarMax prices in round, model-year-and-mileage-keyed steps, so this does happen among its
/// own inventory), not one car recorded twice, since a single run's own detail pages are never the same
/// car under two different files; a tie like that is left alone rather than trusted.</para>
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
        List<Candidate> allPostings = [];
        foreach (PostingEntity posting in carMaxPostings)
        {
            if (ToCandidate(posting) is { } candidate)
            {
                allPostings.Add(candidate);
            }
        }

        // A posting whose dealer is already a real store and whose availability is already read
        // needed nothing from this run to begin with; it never enters the matching below at all.
        int alreadySet = allPostings.Count(c => !c.NeedsMatch);
        List<Candidate> candidates = [.. allPostings.Where(c => c.NeedsMatch)];

        // A fingerprint any two CarMax postings both carry, candidate or not, can't be told apart from
        // a recorded page by fingerprint alone, so neither is touched by the fallback path below: a
        // posting a --revisit already resolved still proves the same collision for a candidate that
        // still shares its fingerprint. A candidate anchored by its own URL never consults this at all.
        HashSet<CarMaxDetailFingerprint> ambiguousFingerprints =
        [
            .. allPostings
                .GroupBy(c => c.Fingerprint)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key),
        ];

        (List<RecordedPage> recordedPages, Dictionary<string, List<RecordedPage>> pagesByUrl) = ScanRecordings(dataDirectory);

        int filled = 0;
        int couldNotMatch = 0;

        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryFindMatch(candidate, recordedPages, pagesByUrl, ambiguousFingerprints, allPostings, out RecordedMatch match))
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
        return new Candidate(posting.Id, posting.Url, fingerprint, observedPrices, isBareDealer, hasAvailability);
    }

    private static readonly JsonSerializerOptions CardEntryOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CardEntry(string? Href, string? Text, string? Card);

    /// <summary>Every recorded CarMax detail page under <paramref name="dataDirectory"/>'s
    /// <c>walks/carmax/&lt;run&gt;/&lt;pair&gt;/detail-N.txt</c> files, read once, alongside a map from a
    /// posting's own URL to every recorded page anchored to it by that URL turning up as an href in the
    /// same pair folder's own <c>cards*.json</c> (see the class doc). The flat list is kept rather than
    /// keyed by fingerprint: <see cref="TryFindMatch"/>'s fallback path compares a candidate against a
    /// page's fingerprint with tolerances a plain dictionary lookup can't express, and needs every page a
    /// candidate could plausibly match, not only the ones an exact fingerprint would have kept.</summary>
    private static (List<RecordedPage> Pages, Dictionary<string, List<RecordedPage>> PagesByUrl) ScanRecordings(string dataDirectory)
    {
        var pages = new List<RecordedPage>();
        var pagesByUrl = new Dictionary<string, List<RecordedPage>>();
        string carMaxRoot = Path.Combine(dataDirectory, "walks", "carmax");
        if (!Directory.Exists(carMaxRoot))
        {
            return (pages, pagesByUrl);
        }

        foreach (string runDirectory in Directory.EnumerateDirectories(carMaxRoot))
        {
            string runFolder = Path.GetFileName(runDirectory);
            foreach (string pairDirectory in Directory.EnumerateDirectories(runDirectory))
            {
                var pairPages = new List<(CarMaxDetailFingerprint Fingerprint, RecordedPage Page)>();
                foreach (string detailFile in Directory.EnumerateFiles(pairDirectory, "detail-*.txt"))
                {
                    string text = File.ReadAllText(detailFile);
                    if (CarMaxDetailFingerprints.Read(text) is not { } fingerprint)
                    {
                        continue;
                    }

                    var page = new RecordedPage(fingerprint, runFolder, CarMaxStores.Read(text), CarMaxStores.ReadAvailability(text));
                    pages.Add(page);
                    pairPages.Add((fingerprint, page));
                }

                foreach ((string href, CarMaxDetailFingerprint cardFingerprint) in ReadCardFingerprints(pairDirectory))
                {
                    List<RecordedPage> owners = [.. pairPages.Where(p => p.Fingerprint == cardFingerprint).Select(p => p.Page)];
                    if (owners.Count != 1)
                    {
                        // Either this link was never visited this run (no detail page recorded for it)
                        // or two of the pair's own detail pages happen to share the card's exact
                        // fingerprint: either way there's nothing on disk to anchor this href to.
                        continue;
                    }

                    string canonicalHref = WalkSites.CanonicalDetailUrl(href);
                    if (!pagesByUrl.TryGetValue(canonicalHref, out List<RecordedPage>? owningPages))
                    {
                        owningPages = [];
                        pagesByUrl[canonicalHref] = owningPages;
                    }

                    owningPages.Add(owners[0]);
                }
            }
        }

        return (pages, pagesByUrl);
    }

    /// <summary>Every (href, fingerprint) pair every <c>cards*.json</c> file in
    /// <paramref name="pairDirectory"/> states, one per distinct href (an href a card's several anchors
    /// all point at, "View more", "Compare", the title, is only read once, from whichever entry names it
    /// first): a card whose own text carries no readable fingerprint is skipped, the same as a detail
    /// page that carries none.</summary>
    private static IEnumerable<(string Href, CarMaxDetailFingerprint Fingerprint)> ReadCardFingerprints(string pairDirectory)
    {
        var seenHrefs = new HashSet<string>();
        foreach (string cardsFile in Directory.EnumerateFiles(pairDirectory, "cards*.json"))
        {
            List<CardEntry>? entries;
            try
            {
                entries = JsonSerializer.Deserialize<List<CardEntry>>(File.ReadAllText(cardsFile), CardEntryOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (CardEntry entry in entries ?? [])
            {
                if (entry.Href is not { Length: > 0 } href || !seenHrefs.Add(href))
                {
                    continue;
                }

                if (entry.Card is { } cardText && CarMaxDetailFingerprints.Read(cardText) is { } fingerprint)
                {
                    yield return (href, fingerprint);
                }
            }
        }
    }

    /// <summary>Whether <paramref name="matches"/> disagree about the store, the availability, or come
    /// from more than one detail page in the very same run: proof they're recordings of two different
    /// cars that merely tie, not the same car recorded more than once (see the class doc), so
    /// <paramref name="matches"/> can't be trusted as a group at all.</summary>
    private static bool HasDisqualifyingDisagreement(List<RecordedPage> matches)
    {
        bool sameRunTwice = matches.GroupBy(p => p.RunFolder).Any(g => g.Count() > 1);
        int distinctStores = matches
            .Select(p => p.Store)
            .OfType<ResolvedDealer>()
            .Select(store => (store.Name, store.Location))
            .Distinct()
            .Count();
        int distinctAvailabilities = matches.Select(p => p.Availability).Distinct().Count();

        return sameRunTwice || distinctStores > 1 || distinctAvailabilities > 1;
    }

    /// <summary>Resolves <paramref name="candidate"/> to the one <paramref name="match"/> to trust, or
    /// false when there is none to trust (see the class doc for the full set of reasons). A candidate
    /// whose own URL is anchored to at least one recorded page (<paramref name="pagesByUrl"/>) is
    /// resolved from that anchored set alone, ahead of and instead of the fingerprint-tolerant fallback
    /// below: a posting's own URL can only ever be its own page, so neither the ambiguous-fingerprint
    /// check nor the cross-posting usable-match check, both about fingerprint tolerance, applies to it.</summary>
    private static bool TryFindMatch(
        Candidate candidate,
        List<RecordedPage> recordedPages,
        Dictionary<string, List<RecordedPage>> pagesByUrl,
        HashSet<CarMaxDetailFingerprint> ambiguousFingerprints,
        List<Candidate> allPostings,
        out RecordedMatch match)
    {
        if (pagesByUrl.TryGetValue(candidate.Url, out List<RecordedPage>? anchoredMatches) && anchoredMatches.Count > 0)
        {
            if (HasDisqualifyingDisagreement(anchoredMatches))
            {
                match = default;
                return false;
            }

            RecordedPage anchoredNewest = anchoredMatches.Aggregate((a, b) => string.CompareOrdinal(a.RunFolder, b.RunFolder) >= 0 ? a : b);
            match = new RecordedMatch(anchoredNewest.Store, anchoredNewest.Availability);
            return true;
        }

        if (ambiguousFingerprints.Contains(candidate.Fingerprint))
        {
            match = default;
            return false;
        }

        List<RecordedPage> matches = [.. recordedPages.Where(page => Matches(candidate, page.Fingerprint))];
        if (matches.Count == 0 || HasDisqualifyingDisagreement(matches))
        {
            match = default;
            return false;
        }

        // A page that also tolerantly matches some other posting can never be the one this candidate is
        // filled from: the same tolerances that let this candidate's own drifted mileage or price still
        // find its page (see the class doc) can just as easily let that other posting's page be this
        // one's instead. It wasn't dropped above, since its disagreement about the store or availability
        // still had to be able to veto the match; with none found, only a page trustworthy enough to
        // actually name a source is left standing here.
        List<RecordedPage> usableMatches =
            [.. matches.Where(page => allPostings.Count(p => Matches(p, page.Fingerprint)) <= 1)];
        if (usableMatches.Count == 0)
        {
            match = default;
            return false;
        }

        RecordedPage newest = usableMatches.Aggregate((a, b) => string.CompareOrdinal(a.RunFolder, b.RunFolder) >= 0 ? a : b);
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
        return hybridIndex < 0
            ? model
            : model[..hybridIndex];
    }

    /// <summary>CarMax rounds any mileage at or above 1,000 to the nearest thousand on its own pages
    /// (see <see cref="CarMaxDetailFingerprints"/>), so a ledger mileage a later sighting from a
    /// different, more exact source has since overwritten is rounded the same way before it's compared
    /// to one.</summary>
    private static int CarMaxRoundedMileage(int mileage) =>
        mileage < 1000
            ? mileage
            : (int)Math.Round(mileage / 1000.0, MidpointRounding.AwayFromZero) * 1000;

    private readonly record struct Candidate(int PostingId, string Url, CarMaxDetailFingerprint Fingerprint, IReadOnlyList<decimal> ObservedPrices, bool IsBareDealer, bool HasAvailability)
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
