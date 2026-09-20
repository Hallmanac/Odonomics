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

    public VehicleEntity? Vehicle { get; set; }
    public List<PriceObservationEntity> PriceObservations { get; set; } = [];
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

    /// <summary>Comma-separated posting sources this run actually queried or walked (e.g.
    /// "auto.dev,marketcheck" or "cars.com"). A source a run could not reach (a missing API key,
    /// an unwalked site) is never listed here, so "still active"/"gone" comparisons only ever
    /// judge a posting against a run that could actually have seen it.</summary>
    public required string Sources { get; set; }
}

/// <summary>The NHTSA vPIC decode and safety lookups for one VIN, refreshed by `odo show` (v0
/// keeps the most recent decode only; the raw JSON is kept for anything the typed fields do not
/// carry yet).</summary>
public sealed class VinRecordEntity
{
    public required string Vin { get; set; }
    public required DateTimeOffset DecodedAt { get; set; }
    public required string DecodeRawJson { get; set; }
    public int OpenRecallCount { get; set; }
    public string? RecallsRawJson { get; set; }
    public int ComplaintCount { get; set; }

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
/// search/walk diff need answered per-source rather than against whichever run happened last.</summary>
public static class RunSources
{
    public static string Join(IEnumerable<string> sources) => string.Join(',', sources);

    public static string[] Split(RunEntity run) => run.Sources.Split(',', StringSplitOptions.RemoveEmptyEntries);

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
