using Odonomics.Auctions;
using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>A vehicle keyed by VIN. Mileage, year, make, model, and trim reflect the most recently
/// observed candidate; nothing here is ever deleted.</summary>
public sealed class VehicleEntity
{
    public required string Vin { get; set; }
    public required int Year { get; set; }
    public required string Make { get; set; }
    public required string Model { get; set; }
    public string? Trim { get; set; }
    public required int Mileage { get; set; }
    public required DateTimeOffset FirstSeen { get; set; }
    public required DateTimeOffset LastSeen { get; set; }
    public DateTimeOffset? FinalistMarkedAt { get; set; }

    /// <summary>Whether the car has smart-key (proximity) entry, as a listing's window sticker or factory
    /// equipment list stated it; unknown until a walk reads one. A dealer's description never sets it. The
    /// factory trim table fills an unknown one at rank time and is not stored here (see
    /// <see cref="FactoryTrimTable"/>), so <see cref="SmartKeyEntrySource"/> is only ever none or the window
    /// sticker.</summary>
    public EquipmentStatus SmartKeyEntry { get; set; }

    public EquipmentSource SmartKeyEntrySource { get; set; }

    /// <summary>Whether the car has push-button start, stored the same way as <see cref="SmartKeyEntry"/>.</summary>
    public EquipmentStatus PushButtonStart { get; set; }

    public EquipmentSource PushButtonStartSource { get; set; }

    /// <summary>The two stored statuses as one value, for the trim table to fill and the scorer to read.</summary>
    public VehicleEquipment StoredEquipment => new(
        new EquipmentFact(SmartKeyEntry, SmartKeyEntrySource),
        new EquipmentFact(PushButtonStart, PushButtonStartSource));

    public List<PostingEntity> Postings { get; set; } = [];
    public List<NoteEntity> Notes { get; set; } = [];
    public VinRecordEntity? VinRecord { get; set; }
    public AuctionCheckEntity? AuctionCheck { get; set; }
}

/// <summary>One source-and-URL sighting of a vehicle. <see cref="LastSeen"/> is stamped with the
/// owning run's own timestamp (not wall-clock time), so a diff can find "gone" postings by
/// comparing LastSeen against the previous run's timestamp rather than any elapsed-time
/// heuristic.</summary>
public sealed class PostingEntity
{
    public int Id { get; set; }
    public required string VehicleVin { get; set; }
    public required string Source { get; set; }
    public required string Url { get; set; }
    public required DateTimeOffset FirstSeen { get; set; }
    public required DateTimeOffset LastSeen { get; set; }
    public int? DealerId { get; set; }

    /// <summary>The one-time shipping fee the latest sighting showed for this posting, on top of
    /// the asking price: 0 for a listing that ships free or is at a nearby store, null when the source
    /// shows none (every site but carvana and carmax). It lives here and not on <see cref="PriceObservationEntity"/> because it
    /// is not part of the asking price history, only of what the car costs to take home; the latest
    /// sighting's value replaces the last one.</summary>
    public decimal? ShippingFee { get; set; }

    /// <summary>How the site says this posting's price relates to its fees, one of the
    /// <see cref="FeePostures"/> strings: null when no run has read it. It is a typed column because
    /// the cost model and the red flags read it, along with <see cref="ItemizedFeesTotal"/>,
    /// <see cref="PickupFee"/>, and <see cref="PickupLocation"/>.</summary>
    public string? FeePosture { get; set; }

    /// <summary>The sum of the fee lines the site itemized: on top of the asking price when its
    /// <see cref="FeePosture"/> is itemized (and then added to what the car costs), or inside it when
    /// the posture is all-in (and then only shown); null when none were read.</summary>
    public decimal? ItemizedFeesTotal { get; set; }

    /// <summary>The title-brand phrase the listing's own detail text stated, such as "rebuilt title" or
    /// "Title status: Salvage", exactly as the page printed it (see <see cref="Walk.TitleBrandStatements"/>);
    /// null when the latest detail visit read none, which is not a claim that the title is clean. It is a
    /// typed column because a red flag reads it. Each detail visit replaces the last one's value, since a
    /// page that has dropped the wording is a fresh reading, and a visit-less card touch leaves it alone.</summary>
    public string? TitleBrandPhrase { get; set; }

    /// <summary>The fee for picking the car up instead of having it delivered, recorded beside
    /// <see cref="ShippingFee"/> as an option and never as a default; null when the site offers none
    /// or none was read.</summary>
    public decimal? PickupFee { get; set; }

    /// <summary>Where the car would be picked up: the city a carmax card names for a car at a nearby store
    /// (with <see cref="ShippingFee"/> 0 and no <see cref="PickupFee"/>), or the place <see cref="PickupFee"/>
    /// is charged for; null when unknown.</summary>
    public string? PickupLocation { get; set; }

    /// <summary>The StartedAt of the run whose detail visit found this posting's page saying the car
    /// has sold (see <see cref="LedgerUpsertService.MarkSoldAsync"/>), or null when no run has. A
    /// sold page never touches <see cref="LastSeen"/>, so the diff finds the posting gone, and a
    /// value equal to the current run's StartedAt is what tells it the car sold rather than merely
    /// dropped off the search.</summary>
    public DateTimeOffset? SoldSeenAt { get; set; }

    /// <summary>The StartedAt of the most recent run whose render wait gave up on this posting's own
    /// search card while the ledger already held it (see
    /// <see cref="LedgerUpsertService.MarkCardsUnrenderedAsync"/>), or null when that has never
    /// happened. A run that gives up this way never touches <see cref="LastSeen"/>, since it never
    /// actually measured the car, so the diff reports the posting gone under "card never rendered"
    /// rather than folding it into whatever reason the rest of its pair's untouched postings get; and
    /// <see cref="VehiclePricing"/> keeps counting it active exactly as it would a posting "beyond the
    /// cap", even once a later run's full coverage of the pair would otherwise have dropped it.</summary>
    public DateTimeOffset? CardUnrenderedSeenAt { get; set; }

    /// <summary>The StartedAt of the most recent run whose detail visit explicitly cleared the
    /// <see cref="PostingAttributeNames.Availability"/> attribute (see
    /// <see cref="LedgerUpsertService.ApplyAttributes"/> and <see cref="ListingCandidate.AttributesToClear"/>),
    /// or null when that has never happened. Stamped whether or not an attribute row actually existed
    /// to remove: it is the only trace left on disk that a page positively read "not reserved" at a
    /// given time, since <see cref="Attributes"/> itself never distinguishes "never read" from "read
    /// and cleared". <see cref="Walk.CarMaxBackfill"/> reads it so an older recorded page that still
    /// shows a reservation can never overwrite a state a newer, unrecorded detail visit already
    /// cleared.</summary>
    public DateTimeOffset? AvailabilityClearedAt { get; set; }

    public VehicleEntity? Vehicle { get; set; }
    public DealerEntity? Dealer { get; set; }
    public List<PriceObservationEntity> PriceObservations { get; set; } = [];
    public List<PostingAttributeEntity> Attributes { get; set; } = [];
}

/// <summary>The values <see cref="PostingEntity.FeePosture"/> takes. A posting whose fees have never
/// been read has a null posture, which is different from <see cref="Unknown"/>: unknown says a run
/// read the page and could not tell.</summary>
public static class FeePostures
{
    /// <summary>The site's price already includes its fees.</summary>
    public const string AllIn = "all-in";

    /// <summary>The site lists its fees separately, on top of the price.</summary>
    public const string Itemized = "itemized";

    /// <summary>A run read the page and could not tell which of the two it is.</summary>
    public const string Unknown = "unknown";
}

/// <summary>One display-only fact a site showed about a posting, such as a badge or a rating,
/// keyed by <see cref="Name"/>. Nothing arithmetic or a red flag reads lives here (that is a typed
/// column on <see cref="PostingEntity"/>), so a value is just text for `odo show` to print. A posting
/// holds at most one row per name: a later observation replaces the earlier one, and
/// <see cref="ObservedRunId"/> names the run that made it.</summary>
public sealed class PostingAttributeEntity
{
    public int Id { get; set; }
    public required int PostingId { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required int ObservedRunId { get; set; }

    public PostingEntity? Posting { get; set; }
}

/// <summary>The names the walk gives the display-only facts it reads off a result card, for
/// <see cref="PostingAttributeEntity.Name"/>. A posting belongs to one site, so a name does not repeat
/// the site: cars.com's deal badge and carvana's are both <see cref="Deal"/>, and the posting's own
/// source says whose opinion it is. Nothing here is read by the cost model or the ranking; a site's
/// price opinion is untested until ledger history shows whether badged cars sell faster or drop less.</summary>
public static class PostingAttributeNames
{
    /// <summary>The site's own verdict on the price: "Great Deal", "Good Deal", "Fair Deal", "Great Price", or "Good Price".</summary>
    public const string Deal = "deal";

    /// <summary>The site's "High Demand" badge (cars.com).</summary>
    public const string Demand = "demand";

    /// <summary>The dealer's rating on the site's own scale, as the card prints it, such as "4.9" (cars.com).</summary>
    public const string DealerRating = "dealer-rating";

    /// <summary>The site's "Price Drop" badge (autotrader and carvana).</summary>
    public const string PriceDrop = "price-drop";

    /// <summary>The "Online Paperwork" badge (autotrader).</summary>
    public const string Paperwork = "paperwork";

    /// <summary>The "Free shipping" badge (carvana).</summary>
    public const string Shipping = "shipping";

    /// <summary>The "Dealer Fees Included" badge (autotrader).</summary>
    public const string FeesIncluded = "fees-included";

    /// <summary>The "No Accidents" badge (autotrader).</summary>
    public const string NoAccidents = "no-accidents";

    /// <summary>What a CarMax detail page's own header says about a posting that cannot be bought
    /// like an ordinary listing on the lot: <see cref="Odonomics.Walk.CarMaxStores.Reserved"/> when
    /// the header read "Reserved at" (held for another buyer) or
    /// <see cref="Odonomics.Walk.CarMaxStores.ComingSoon"/> when it read "Coming to" (still in
    /// transit). Unlike every other name here, this one is not display only: `odo rank` and `odo show`
    /// never price a vehicle from a posting flagged this way, and a vehicle with no other purchasable,
    /// priced posting is excluded outright (see <see cref="Odonomics.Ledger.VehiclePricing.OnlyReservedOrInTransit"/>).</summary>
    public const string Availability = "availability";
}

/// <summary>A selling dealer, keyed by its normalized name and location (see
/// <see cref="DealerNormalizer"/>) so the same dealer named slightly differently across sources or
/// runs still resolves to one row. <see cref="Grade"/>, <see cref="GradeReason"/>, and
/// <see cref="GradeCheckedAt"/> come from `odo dealer grade`: <see cref="GradeCheckedAt"/> is
/// stamped the moment CarEdge is checked regardless of whether it had a rating, which is what lets
/// a dealer CarEdge has no rating for stay ungraded without being looked up again on every later
/// run. <see cref="DocFee"/> and <see cref="AddOnsNote"/> are what CarEdge's card printed for the
/// dealer when it was graded; they stay null on a dealer graded before the ledger kept them until a
/// refresh (`odo dealer grade --all --refresh`) reads the card again.</summary>
public sealed class DealerEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Location { get; set; }
    public required string NormalizedName { get; set; }
    public required string NormalizedLocation { get; set; }
    public string? Grade { get; set; }
    public string? GradeReason { get; set; }
    public DateTimeOffset? GradeCheckedAt { get; set; }

    /// <summary>The dealer's documentation fee in dollars, as CarEdge printed it on the card.</summary>
    public decimal? DocFee { get; set; }

    /// <summary>CarEdge's add-ons line for the dealer, as printed ("No add-ons" or "$358 add-ons").</summary>
    public string? AddOnsNote { get; set; }

    public List<PostingEntity> Postings { get; set; } = [];
}

/// <summary>Append-only: one row per run where the price was observed, written only on the first
/// sighting of a posting or when the price actually changed.</summary>
public sealed class PriceObservationEntity
{
    public int Id { get; set; }
    public required int PostingId { get; set; }
    public required decimal Price { get; set; }
    public required DateTimeOffset ObservedAt { get; set; }

    public PostingEntity? Posting { get; set; }
}

/// <summary>One invocation of `odo search` or `odo walk`.</summary>
public sealed class RunEntity
{
    public int Id { get; set; }
    public required string Command { get; set; }
    public required DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Comma-separated "source:model" tokens (see <see cref="RunSources.Key"/>) this run
    /// actually got a usable result for, e.g. "auto.dev:Camry Hybrid,marketcheck:Camry Hybrid" or
    /// "cars.com:Insight". A source/model pair the run could not reach or check successfully (a
    /// missing API key, a failed query, an unwalked site or model) is never listed here, so "still
    /// active"/"gone" comparisons only ever judge a posting against a run that could actually have
    /// seen it.</summary>
    public required string Sources { get; set; }

    /// <summary>The scenario zip this run searched with. Null on a run recorded before the ledger
    /// kept it, in which case a later run cannot tell whether the search area moved.</summary>
    public string? Zip { get; set; }

    /// <summary>The scenario radius, in miles, this run searched with. Null on a run recorded
    /// before the ledger kept it.</summary>
    public int? RadiusMiles { get; set; }
}

/// <summary>The NHTSA vPIC decode and safety lookups, and the Marketcheck VIN history, for one VIN,
/// refreshed by `odo show` and `odo research` (v0 keeps the most recent fetch only; the raw JSON is
/// kept for anything the typed fields do not carry yet). <see cref="ResearchedAt"/> is the
/// fetched-at stamp that gates the seven-day refresh rule for the whole NHTSA batch (decode,
/// recalls, complaints, safety ratings) and the VIN history, and is what `odo rank`'s research
/// column reads to decide "researched" vs. "not researched"; <see cref="VinResearchService.RefreshAsync"/>
/// only stamps it when at least one of recalls, complaints, or safety ratings actually came back, so
/// a vehicle NHTSA could not answer at all (every piece could-not-fetch) is never shown as
/// researched. Recalls, complaints, and safety
/// ratings each also carry their own FetchedAt stamp and CouldNotFetchReason: when NHTSA answers
/// one of those calls with a bad response (see NhtsaClient's retry-then-degrade behavior), that
/// piece alone is marked could-not-fetch and the next research run retries only it, rather than
/// waiting out the seven-day rule or re-fetching pieces that already succeeded.</summary>
public sealed class VinRecordEntity
{
    public required string Vin { get; set; }
    public required DateTimeOffset DecodedAt { get; set; }
    public required string DecodeRawJson { get; set; }
    public int OpenRecallCount { get; set; }
    public string? RecallsRawJson { get; set; }
    public DateTimeOffset? RecallsFetchedAt { get; set; }
    public string? RecallsCouldNotFetchReason { get; set; }
    public int ComplaintCount { get; set; }
    public DateTimeOffset? ComplaintsFetchedAt { get; set; }
    public string? ComplaintsCouldNotFetchReason { get; set; }

    public DateTimeOffset? ResearchedAt { get; set; }
    public int? SafetyOverallRating { get; set; }
    public int? SafetyFrontRating { get; set; }
    public int? SafetySideRating { get; set; }
    public int? SafetyRolloverRating { get; set; }
    public string? SafetyRawJson { get; set; }
    public DateTimeOffset? SafetyFetchedAt { get; set; }
    public string? SafetyCouldNotFetchReason { get; set; }
    public string? HistoryRawJson { get; set; }
    public int? CurrentListingDaysOnMarket { get; set; }
    public DateTimeOffset? HistoryFetchedAt { get; set; }
    public string? HistoryCouldNotFetchReason { get; set; }

    public VehicleEntity? Vehicle { get; set; }
}

/// <summary>What `odo title check` last found out about a vehicle in the public salvage-auction
/// archives (see <c>Odonomics.Auctions</c>): that a Copart or IAA sale was found, with its fields,
/// that none was, or that the archives could not be read, with a reason such as a captcha. Kept apart
/// from <see cref="VinRecordEntity"/> because it comes from a browser lookup and not the NHTSA and
/// Marketcheck research, and has its own recheck rule (see <see cref="AuctionChecks.NeedsCheck"/>).
/// The sale fields are null unless <see cref="Outcome"/> is found, and <see cref="CouldNotReadReason"/>
/// is null unless it is could-not-read.</summary>
public sealed class AuctionCheckEntity
{
    public required string Vin { get; set; }
    public required AuctionCheckOutcome Outcome { get; set; }
    public required DateTimeOffset CheckedAt { get; set; }
    public string? CouldNotReadReason { get; set; }
    public string? Auction { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? SaleDate { get; set; }
    public string? SaleDocument { get; set; }
    public string? PrimaryDamage { get; set; }
    public string? SecondaryDamage { get; set; }
    public decimal? Acv { get; set; }
    public decimal? RepairEstimate { get; set; }
    public int? Odometer { get; set; }
    public string? SourceUrl { get; set; }

    public VehicleEntity? Vehicle { get; set; }
}

/// <summary>One one-time ledger data migration that has already run, recorded by name so
/// <see cref="LedgerDataMigrations"/> never re-applies it on a later startup.</summary>
public sealed class LedgerMigrationEntity
{
    public required string Name { get; set; }
    public required DateTimeOffset AppliedAt { get; set; }
}

/// <summary>A free-text note attached to a vehicle: PPI results, a Carfax/AutoCheck summary,
/// anything the operator wants on record before marking a finalist.</summary>
public sealed class NoteEntity
{
    public int Id { get; set; }
    public required string VehicleVin { get; set; }
    public required string Text { get; set; }
    public required DateTimeOffset CreatedAt { get; set; }

    public VehicleEntity? Vehicle { get; set; }
}

/// <summary>Reads and writes <see cref="RunEntity.Sources"/>'s comma-separated list, and answers
/// "as of which run did this source last get checked", the question both the rank view and the
/// search/walk diff need answered per-source rather than against whichever run happened last.
/// Every token is a "source:model" pair (see <see cref="Key"/>), not a bare source name: a run
/// only ever confirms one or more specific models for a source (one for a walk, whichever queries
/// actually succeeded for a search), never the source as a whole, so coverage has to be scoped
/// that finely or a run that only checked one model would silently vouch for every other model
/// that happens to share its source too.</summary>
public static class RunSources
{
    /// <summary>The token for one (source, model) pair actually covered by a run, where model is
    /// the bare model name (e.g. "Insight", "Camry Hybrid"), matching
    /// <see cref="VehicleEntity.Model"/>.</summary>
    public static string Key(string source, string model) => $"{source}:{model}";

    public static string Join(IEnumerable<string> sources) => string.Join(',', sources);

    /// <summary>The prefix of the token a walk stamps beside a pair's coverage token when the pair's
    /// link collection stopped before the site ran out of results (an explicit --max filled the pool):
    /// "capped:cars.com:Prius" sits beside "cars.com:Prius". The pair is still covered, so its plain
    /// token stays and every coverage lookup keeps working; the extra token only says the coverage was
    /// partial, so the diff does not read a posting the run never reached as a car that sold.</summary>
    private const string PartialPrefix = "capped:";

    /// <summary>The prefix of the token a walk stamps beside a pair's coverage token when a result page
    /// after the first failed to load, so the pages after it were never read: "unread:carvana:Prius". It
    /// is the same kind of marker as <see cref="PartialPrefix"/> with a reason of its own, so the diff can
    /// say the postings went unread rather than that a cap kept the walk from them.</summary>
    private const string UnreadPrefix = "unread:";

    /// <summary>The prefix of the token a walk stamps beside a pair's <see cref="UnreadPrefix"/> one when
    /// the pair's walk nonetheless read every search page to the site's natural end of results (no --max
    /// stop, no failed page, no search short of its stated count), so the only thing that made the pair
    /// partial was something else, such as a card that never rendered: "swept:cars.com:Prius". The pair
    /// still counts as partial for coverage, but a known posting absent from every page it read is gone
    /// from the search page, not unread.</summary>
    private const string SweptPrefix = "swept:";

    /// <summary>The token that marks the pair behind <paramref name="coverageKey"/> (a <see cref="Key"/>)
    /// as partially covered this run because an explicit --max stopped its link collection short.</summary>
    public static string PartialKey(string coverageKey) => PartialPrefix + coverageKey;

    /// <summary>The token that marks the pair behind <paramref name="coverageKey"/> (a <see cref="Key"/>)
    /// as partially covered this run because a result page after the first failed to load.</summary>
    public static string UnreadKey(string coverageKey) => UnreadPrefix + coverageKey;

    /// <summary>The token that marks the pair behind <paramref name="coverageKey"/> (a <see cref="Key"/>)
    /// as having had every search page read to the site's natural end of results this run, whatever else
    /// stamped it partial.</summary>
    public static string SweptKey(string coverageKey) => SweptPrefix + coverageKey;

    /// <summary>The coverage tokens the run stamped, without the partial-coverage markers (see
    /// <see cref="PartialKey"/> and <see cref="UnreadKey"/>), so a marker is never mistaken for a source
    /// and model pair.</summary>
    public static string[] Split(RunEntity run) =>
        [.. SplitAll(run).Where(token => !IsMarker(token))];

    /// <summary>The <see cref="Key"/> of every pair the run covered only partially because of a cap.</summary>
    public static HashSet<string> PartialCoverage(RunEntity run) => MarkedPairs(run, PartialPrefix);

    /// <summary>The <see cref="Key"/> of every pair the run covered only partially because a later result
    /// page failed to load.</summary>
    public static HashSet<string> UnreadCoverage(RunEntity run) => MarkedPairs(run, UnreadPrefix);

    /// <summary>The <see cref="Key"/> of every pair whose walk read every search page to the site's natural
    /// end of results this run, even one also stamped partial for another reason.</summary>
    public static HashSet<string> SweptCoverage(RunEntity run) => MarkedPairs(run, SweptPrefix);

    /// <summary>Whether the run left any of <paramref name="coverageKey"/>'s pair unread, for either reason.</summary>
    public static bool IsPartial(RunEntity run, string coverageKey) =>
        PartialCoverage(run).Contains(coverageKey) || UnreadCoverage(run).Contains(coverageKey);

    private static HashSet<string> MarkedPairs(RunEntity run, string prefix) =>
        [.. SplitAll(run)
            .Where(token => token.StartsWith(prefix, StringComparison.Ordinal))
            .Select(token => token[prefix.Length..])];

    private static bool IsMarker(string token) =>
        token.StartsWith(PartialPrefix, StringComparison.Ordinal)
        || token.StartsWith(UnreadPrefix, StringComparison.Ordinal)
        || token.StartsWith(SweptPrefix, StringComparison.Ordinal);

    private static string[] SplitAll(RunEntity run) => run.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Splits a <see cref="Key"/> token back into its source and model. Only the first
    /// colon is significant; a model name is never expected to contain one.</summary>
    public static (string Source, string Model) SplitKey(string token)
    {
        int colonIndex = token.IndexOf(':');
        return colonIndex < 0 ? (token, "") : (token[..colonIndex], token[(colonIndex + 1)..]);
    }

    /// <summary>The runs whose sightings of <paramref name="token"/>'s pair are still current, newest
    /// first: the latest run that covered the pair, then, for as long as the run before covered it only
    /// partially (see <see cref="PartialKey"/> and <see cref="UnreadKey"/>), the one before that, ending
    /// with the first that covered it in full. A partial run never looked for the postings it did not reach, so it does not
    /// supersede the coverage that did; a posting last seen by any run in this chain was still listed
    /// when the newest of them ended. Empty when no run covered the pair.</summary>
    public static List<RunEntity> CoverageChain(string token, IEnumerable<RunEntity> runs)
    {
        List<RunEntity> chain = [];
        foreach (RunEntity run in runs.Where(r => Split(r).Contains(token)).OrderByDescending(r => r.StartedAt))
        {
            chain.Add(run);
            if (!IsPartial(run, token))
            {
                break;
            }
        }

        return chain;
    }

    /// <summary>For every source and model pair covered by any run in <paramref name="runs"/>, the
    /// StartedAt of the latest run that covered it in full. A run that covered the pair only partially
    /// (see <see cref="PartialKey"/> and <see cref="UnreadKey"/>) does not replace it, so the postings that run never reached keep
    /// counting as listed; a pair only ever covered partially reports its first partial run. A posting
    /// whose LastSeen is at or after this time is still listed as far as the ledger knows.</summary>
    public static Dictionary<string, DateTimeOffset> LatestCoverageBySource(IEnumerable<RunEntity> runs)
    {
        List<RunEntity> allRuns = [.. runs];
        return allRuns
            .SelectMany(Split)
            .Distinct()
            .ToDictionary(token => token, token => CoverageChain(token, allRuns)[^1].StartedAt);
    }
}
