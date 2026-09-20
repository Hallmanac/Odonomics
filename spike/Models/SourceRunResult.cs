namespace Spike.Models;

public sealed class SourceRunResult
{
    public required string Source { get; init; }
    public bool CouldNotRun { get; set; }
    public string? CouldNotRunReason { get; set; }

    /// <summary>True when a bot-defense challenge stopped this page walk at some point, even if
    /// it still produced usable search-page coverage stats. Drives the Cars.com to Autotrader
    /// fallback; distinct from <see cref="CouldNotRun"/>, which means nothing usable came back.</summary>
    public bool WasBlocked { get; set; }
    public TimeSpan WallTime { get; set; }
    public decimal DollarsSpent { get; set; }
    public List<Candidate> Candidates { get; } = [];
    public List<string> Failures { get; } = [];

    /// <summary>
    /// For a page-walk source, the raw count of distinct listing links found on search pages,
    /// before any of them were confirmed to match the query or even visited. Always reported as
    /// <see cref="CandidatesFound"/> for a page-walk source, in place of the query-matching count
    /// <see cref="Candidates"/> would otherwise give: it is usually larger, sometimes much larger,
    /// since it counts every link the site returned (including wrong-model listings a site
    /// returned because it silently ignored the model filter) rather than only the ones extraction
    /// went on to confirm. Null for API sources, where a candidate is only ever known once fully
    /// parsed, so <see cref="Candidates"/> is the only count that exists.
    /// </summary>
    public int? SearchOnlyCandidatesFound { get; set; }

    /// <summary>
    /// The "Candidates found" report column. For an API source this is the query-matching count
    /// from <see cref="Candidates"/>; for a page-walk source it is the raw
    /// <see cref="SearchOnlyCandidatesFound"/> instead, which is not filtered by query match, by
    /// VIN presence, or by whether the link was ever visited at all. The two are not the same kind
    /// of number: compare <see cref="CandidatesWithVin"/> across sources for a like-for-like count.
    /// </summary>
    public int CandidatesFound => SearchOnlyCandidatesFound ?? Candidates.Count(c => c.MatchesQuery);
    public int CandidatesWithVin => Candidates.Count(c => c.MatchesQuery && !string.IsNullOrWhiteSpace(c.Vin));

    /// <summary>Set by the orchestrator once every source in the run has reported.</summary>
    public int VinsUniqueToSource { get; set; }
}
