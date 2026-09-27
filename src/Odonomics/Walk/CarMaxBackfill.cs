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
/// the two were read from the very same walk, so nothing about either has had a chance to drift yet.
/// That anchor only ever forms when the card's own fingerprint is unique among the pair's own cards,
/// though: two different cards in the same pair can share one (CarMax prices in round,
/// model-year-and-mileage-keyed steps, so this does happen), and a fingerprint like that can't tell
/// their hrefs apart from each other, so neither is anchored to any detail page that shares it, however
/// certain that page otherwise looks.</para>
///
/// <para>An anchor to the candidate's own URL is the only match this backfill ever trusts. An earlier
/// version also matched by fingerprint alone (year, make, model, trim, mileage, and price, with no URL
/// in it at all) whenever no anchor could be formed, on the theory that two cars agreeing on every one
/// of those figures at once was rare enough to trust. It wasn't rare enough: a ledger vehicle row can
/// drift after the fact (a later, more exact source overwrites a shared VIN's mileage or model; see
/// lesson 84abc33f), far enough that one car's own figures now read as some other, merely
/// similar-looking car's, and that fallback would then fill a posting from a stranger's recorded page
/// with nothing on disk left to catch it. Patching one drifted field at a time doesn't make that risk go
/// away, so this backfill no longer tries: a candidate whose URL never turns up as an anchored href is
/// left alone, bare, and counted as could-not-match, whatever its fingerprint happens to resemble. The
/// next ordinary <c>odo walk carmax</c> re-discovers its own card and fills it the ordinary way instead.</para>
///
/// <para>When a candidate's own URL ties more than one recorded page (the same car walked more than once,
/// across different runs, each one's card and detail page recorded in its own run's pair folder), the page
/// from the newest run wins, whether or not the pages agree about the store or the availability: every
/// page in the set carries the candidate's own canonical URL, so all of them are certainly its own car,
/// and a disagreement between them is simply the car changing between runs, not proof of anything else.</para>
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

        Dictionary<string, List<RecordedPage>> pagesByUrl = ScanRecordings(dataDirectory);

        int filled = 0;
        int couldNotMatch = 0;

        foreach (Candidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryFindMatch(candidate, pagesByUrl, out RecordedMatch match))
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
        if (posting.Vehicle is null)
        {
            return null;
        }

        bool isBareDealer = posting.Dealer is null
            || (posting.Dealer.NormalizedName == DealerNormalizer.Normalize(WalkSites.CarMaxDealerName)
                && string.IsNullOrEmpty(posting.Dealer.Location));
        bool hasAvailability = posting.Attributes.Any(a => a.Name == PostingAttributeNames.Availability);

        return new Candidate(posting.Id, posting.Url, isBareDealer, hasAvailability);
    }

    private static readonly JsonSerializerOptions CardEntryOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record CardEntry(string? Href, string? Text, string? Card);

    /// <summary>A map from a posting's own URL to every recorded page anchored to it by that URL turning
    /// up as an href in the same pair folder's own <c>cards*.json</c> (see the class doc), read once from
    /// every recorded CarMax detail page under <paramref name="dataDirectory"/>'s
    /// <c>walks/carmax/&lt;run&gt;/&lt;pair&gt;/detail-N.txt</c> files.</summary>
    private static Dictionary<string, List<RecordedPage>> ScanRecordings(string dataDirectory)
    {
        var pagesByUrl = new Dictionary<string, List<RecordedPage>>();
        string carMaxRoot = Path.Combine(dataDirectory, "walks", "carmax");
        if (!Directory.Exists(carMaxRoot))
        {
            return pagesByUrl;
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

                    var page = new RecordedPage(runFolder, CarMaxStores.Read(text), CarMaxStores.ReadAvailability(text));
                    pairPages.Add((fingerprint, page));
                }

                List<(string Href, CarMaxDetailFingerprint Fingerprint)> cardFingerprints = [.. ReadCardFingerprints(pairDirectory)];

                // A fingerprint two or more of the pair's own cards share (CarMax prices in round,
                // model-year-and-mileage-keyed steps, so this does happen) can't tell those cards' hrefs
                // apart from each other, so a detail page with that same fingerprint can't be anchored
                // to any one of them either: doing so risked linking one car's own recorded page to a
                // different car's posting, silently.
                HashSet<CarMaxDetailFingerprint> ambiguousCardFingerprints =
                [
                    .. cardFingerprints
                        .GroupBy(c => c.Fingerprint)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key),
                ];

                foreach ((string href, CarMaxDetailFingerprint cardFingerprint) in cardFingerprints)
                {
                    if (ambiguousCardFingerprints.Contains(cardFingerprint))
                    {
                        continue;
                    }

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

        return pagesByUrl;
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

    /// <summary>Resolves <paramref name="candidate"/> to the one <paramref name="match"/> to trust: every
    /// recorded page anchored to the candidate's own URL (<paramref name="pagesByUrl"/>), newest run
    /// first, or false when its URL was never anchored to any recorded page at all. No disagreement veto
    /// applies here: every page in an anchored set carries the candidate's own canonical URL, so all of
    /// them are certainly its own car, and a disagreement between them is simply the car changing between
    /// runs.</summary>
    private static bool TryFindMatch(
        Candidate candidate,
        Dictionary<string, List<RecordedPage>> pagesByUrl,
        out RecordedMatch match)
    {
        if (!pagesByUrl.TryGetValue(candidate.Url, out List<RecordedPage>? anchoredMatches) || anchoredMatches.Count == 0)
        {
            match = default;
            return false;
        }

        RecordedPage newest = anchoredMatches.Aggregate((a, b) => string.CompareOrdinal(a.RunFolder, b.RunFolder) >= 0 ? a : b);
        match = new RecordedMatch(newest.Store, newest.Availability);
        return true;
    }

    private readonly record struct Candidate(int PostingId, string Url, bool IsBareDealer, bool HasAvailability)
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

    private readonly record struct RecordedPage(string RunFolder, ResolvedDealer? Store, string? Availability);

    private readonly record struct RecordedMatch(ResolvedDealer? Store, string? Availability);
}
