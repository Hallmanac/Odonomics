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
    /// For a page-walk source, the count of distinct listings found on search pages, which can
    /// exceed <see cref="Candidates"/> when detail-page extraction was blocked partway through.
    /// Null for API sources, where a candidate is only ever known once fully parsed.
    /// </summary>
    public int? SearchOnlyCandidatesFound { get; set; }

    public int CandidatesFound => SearchOnlyCandidatesFound ?? Candidates.Count(c => c.MatchesQuery);
    public int CandidatesWithVin => Candidates.Count(c => c.MatchesQuery && !string.IsNullOrWhiteSpace(c.Vin));

    /// <summary>Set by the orchestrator once every source in the run has reported.</summary>
    public int VinsUniqueToSource { get; set; }
}
