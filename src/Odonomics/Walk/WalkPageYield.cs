namespace Odonomics.Walk;

/// <summary>Tells a later page of a paged search that is genuinely running out of results from one whose
/// own rendering looked degraded this run (walk run 20260928-134242, whose cars.com pages came back with
/// far fewer cards than usual without any page ever actually failing to load). Used only for a site with
/// its own <see cref="WalkSite.TypicalResultsPerPage"/> (cars.com, which pads every page to about that many
/// regardless of a search's real match count and states no match count of its own for
/// <see cref="WalkSite.MatchCountPattern"/> to bound collection against), since a site whose stated count
/// already bounds collection has no need of a second, page-size-shaped signal.</summary>
public static class WalkPageYield
{
    /// <summary>Whether <paramref name="rawCardCount"/> (every detail-matching link a page actually
    /// carried, rendered or not) comes in at no more than half of the smaller of
    /// <paramref name="firstPageCardCount"/> (this same search's own first page, which a throttled run's
    /// later pages are checked against directly, rather than only the site's typical size) and
    /// <paramref name="typicalResultsPerPage"/> (<see cref="WalkSite.TypicalResultsPerPage"/>). Taking the
    /// smaller of the two keeps a search whose own first page is already thinner than the site's typical
    /// size (a small facet cars.com still pads, but not all the way to a full page, see
    /// WalkSites.CarsCom's hybrid facet) from being held to a bigger page size than it ever actually had,
    /// while still catching a later page of that same search falling well short of even its own thinner
    /// first page. "At most half", not "less than half": walk run 20260928-134242's own degraded Prius
    /// pages settled at exactly half its typical 30 (15 of them, then 14), so a strict "less than" would
    /// have missed most of the very pages this exists to catch.</summary>
    public static bool FellFar(int rawCardCount, int firstPageCardCount, int typicalResultsPerPage) =>
        rawCardCount * 2 <= Math.Min(firstPageCardCount, typicalResultsPerPage);
}
