using Odonomics.Ledger;

namespace Odonomics.Sources;

public sealed record SourceResult
{
    public required string Source { get; init; }
    public bool CouldNotRun { get; init; }
    public string? CouldNotRunReason { get; init; }
    public List<ListingCandidate> Candidates { get; init; } = [];

    /// <summary>Candidates seen but rejected before reaching the ledger (no VIN, no URL, or
    /// outside the query's year/mileage range), with the reason for each.</summary>
    public List<string> Rejections { get; init; } = [];

    /// <summary>The model (not "Make Model", just the model part, matching
    /// <see cref="Odonomics.Ledger.VehicleEntity.Model"/>) of every query this run actually got a
    /// usable response for, whether or not that response contained any candidates. A query that
    /// failed (a non-success HTTP status, an unparseable or unexpectedly shaped body, a timeout,
    /// or any other exception) is left out, so a caller can tell "checked, found nothing" apart
    /// from "never actually checked" per model rather than treating the whole source as covered
    /// the moment it isn't <see cref="CouldNotRun"/>.</summary>
    public List<string> ModelsCovered { get; init; } = [];
}

public interface IListingSource
{
    string Name { get; }

    Task<SourceResult> RunAsync(IReadOnlyList<ListingQuery> queries, CancellationToken cancellationToken);
}
