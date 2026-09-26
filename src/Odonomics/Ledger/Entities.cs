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

    public List<PostingEntity> Postings { get; set; } = [];
    public List<NoteEntity> Notes { get; set; } = [];
    public VinRecordEntity? VinRecord { get; set; }
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
    /// the asking price: 0 for a listing that ships free, null when the source shows none (every
    /// site but carvana). It lives here and not on <see cref="PriceObservationEntity"/> because it
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

    /// <summary>The fee for picking the car up instead of having it delivered, recorded beside
    /// <see cref="ShippingFee"/> as an option and never as a default; null when the site offers none
    /// or none was read.</summary>
    public decimal? PickupFee { get; set; }

    /// <summary>Where the car would be picked up, for <see cref="PickupFee"/>; null when unknown.</summary>
    public string? PickupLocation { get; set; }

    /// <summary>The StartedAt of the run whose detail visit found this posting's page saying the car
    /// has sold (see <see cref="LedgerUpsertService.MarkSoldAsync"/>), or null when no run has. A
    /// sold page never touches <see cref="LastSeen"/>, so the diff finds the posting gone, and a
    /// value equal to the current run's StartedAt is what tells it the car sold rather than merely
    /// dropped off the search.</summary>
    public DateTimeOffset? SoldSeenAt { get; set; }

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

    /// <summary>The token that marks the pair behind <paramref name="coverageKey"/> (a <see cref="Key"/>)
    /// as partially covered this run because an explicit --max stopped its link collection short.</summary>
    public static string PartialKey(string coverageKey) => PartialPrefix + coverageKey;

    /// <summary>The token that marks the pair behind <paramref name="coverageKey"/> (a <see cref="Key"/>)
    /// as partially covered this run because a result page after the first failed to load.</summary>
    public static string UnreadKey(string coverageKey) => UnreadPrefix + coverageKey;

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

    /// <summary>Whether the run left any of <paramref name="coverageKey"/>'s pair unread, for either reason.</summary>
    public static bool IsPartial(RunEntity run, string coverageKey) =>
        PartialCoverage(run).Contains(coverageKey) || UnreadCoverage(run).Contains(coverageKey);

    private static HashSet<string> MarkedPairs(RunEntity run, string prefix) =>
        [.. SplitAll(run)
            .Where(token => token.StartsWith(prefix, StringComparison.Ordinal))
            .Select(token => token[prefix.Length..])];

    private static bool IsMarker(string token) =>
        token.StartsWith(PartialPrefix, StringComparison.Ordinal) || token.StartsWith(UnreadPrefix, StringComparison.Ordinal);

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
