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

    public VehicleEntity? Vehicle { get; set; }
    public DealerEntity? Dealer { get; set; }
    public List<PriceObservationEntity> PriceObservations { get; set; } = [];
}

/// <summary>A selling dealer, keyed by its normalized name and location (see
/// <see cref="DealerNormalizer"/>) so the same dealer named slightly differently across sources or
/// runs still resolves to one row. <see cref="Grade"/>, <see cref="GradeReason"/>, and
/// <see cref="GradeCheckedAt"/> come from `odo dealer grade`: <see cref="GradeCheckedAt"/> is
/// stamped the moment CarEdge is checked regardless of whether it had a rating, which is what lets
/// a dealer CarEdge has no rating for stay ungraded without being looked up again on every later
/// run.</summary>
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
}

/// <summary>The NHTSA vPIC decode and safety lookups, and the Marketcheck VIN history, for one VIN,
/// refreshed by `odo show` and `odo research` (v0 keeps the most recent fetch only; the raw JSON is
/// kept for anything the typed fields do not carry yet). <see cref="ResearchedAt"/> is the
/// fetched-at stamp that gates the seven-day refresh rule for the safety ratings and VIN history;
/// it is null until the first successful research fetch.</summary>
public sealed class VinRecordEntity
{
    public required string Vin { get; set; }
    public required DateTimeOffset DecodedAt { get; set; }
    public required string DecodeRawJson { get; set; }
    public int OpenRecallCount { get; set; }
    public string? RecallsRawJson { get; set; }
    public int ComplaintCount { get; set; }

    public DateTimeOffset? ResearchedAt { get; set; }
    public int? SafetyOverallRating { get; set; }
    public int? SafetyFrontRating { get; set; }
    public int? SafetySideRating { get; set; }
    public int? SafetyRolloverRating { get; set; }
    public string? SafetyRawJson { get; set; }
    public string? HistoryRawJson { get; set; }
    public int? CurrentListingDaysOnMarket { get; set; }

    public VehicleEntity? Vehicle { get; set; }
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

    public static string[] Split(RunEntity run) => run.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Splits a <see cref="Key"/> token back into its source and model. Only the first
    /// colon is significant; a model name is never expected to contain one.</summary>
    public static (string Source, string Model) SplitKey(string token)
    {
        int colonIndex = token.IndexOf(':');
        return colonIndex < 0 ? (token, "") : (token[..colonIndex], token[(colonIndex + 1)..]);
    }

    /// <summary>For every source covered by any run in <paramref name="runs"/>, the StartedAt of
    /// the most recent one that covered it.</summary>
    public static Dictionary<string, DateTimeOffset> LatestCoverageBySource(IEnumerable<RunEntity> runs)
    {
        var latest = new Dictionary<string, DateTimeOffset>();
        foreach (RunEntity run in runs)
        {
            foreach (string source in Split(run))
            {
                if (!latest.TryGetValue(source, out DateTimeOffset existing) || run.StartedAt > existing)
                {
                    latest[source] = run.StartedAt;
                }
            }
        }

        return latest;
    }
}
