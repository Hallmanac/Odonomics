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
}

public interface IListingSource
{
    string Name { get; }

    Task<SourceResult> RunAsync(IReadOnlyList<ListingQuery> queries, CancellationToken cancellationToken);
}
