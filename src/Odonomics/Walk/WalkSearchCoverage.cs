namespace Odonomics.Walk;

/// <summary>One search's own reported reach against what it actually collected (see
/// <see cref="WalkSearchPages.CollectLinksAsync"/>'s own <c>onSearchCoverageKnown</c> callback):
/// <see cref="Label"/> names which of a multi-search pair's searches this is (a hybrid-only-from-year
/// model's hybrid and base-model searches), <see cref="Collected"/> is how many candidate links it
/// actually considered, and <see cref="Stated"/> is the count its own first page stated, null when
/// that page stated none.</summary>
public sealed record SearchCoverage(string Label, int Collected, int? Stated)
{
    /// <summary>Whether this search reached its own stated count, or came within the tolerance every
    /// paged site's stated count already allows (see <see cref="SearchPageLoadMore.ToleranceFor"/>); a
    /// search with no stated count at all is always full, since there is nothing for it to fall short of.</summary>
    public bool IsFull => Stated is not int stated || Collected >= stated || stated - Collected <= SearchPageLoadMore.ToleranceFor(stated);
}

/// <summary>Turns a pair's own <see cref="SearchCoverage"/> list, one entry per search, into the pair's
/// own coverage line.</summary>
public static class WalkSearchCoverage
{
    /// <summary>The pair's own coverage line, one search's own label, collected, and stated count per
    /// clause ("Camry Hybrid 51 of 49 stated; Camry from 2025 18 of 394 stated"), joined "; "; a search
    /// with no stated count is left out of it, and this is null when none of them stated one at all.</summary>
    public static string? PairLine(IReadOnlyList<SearchCoverage> searches)
    {
        List<string> parts = [.. searches.Where(s => s.Stated is not null).Select(s => $"{s.Label} {s.Collected} of {s.Stated} stated")];
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}
