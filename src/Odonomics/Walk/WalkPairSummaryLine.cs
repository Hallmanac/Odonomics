namespace Odonomics.Walk;

/// <summary>
/// Formats one (site, model) pair's tally into the single line printed right after its walk and
/// into the "Dropped" cell of the end-of-run summary table, so the two always agree with each
/// other and with the reason wording <see cref="WalkOutcomeWording"/> gives the per-page detail
/// lines above them. Saved plus every non-zero dropped reason always sums to pages, since both
/// come from the same <see cref="DroppedBreakdown"/>; the known-from-cards count sits beside them but is
/// not part of that sum, since those links were kept current from their search cards and never visited.
/// Every non-zero reason is always named, in the fixed order <see cref="DroppedReasonOrder"/> gives: the table's own column wraps a long
/// breakdown onto a continuation line under the same row rather than ever falling back to a bare
/// count, since a bare count with no reason is exactly the gap this class exists to close. The
/// per-pair line itself never wraps mid-parenthetical: when it would exceed the console width the
/// whole breakdown moves to an indented continuation line instead. A pair that stopped short of the
/// site's results (an explicit --max ended it) ends its line with ", capped", so a reader knows the
/// tally is not the whole site, and one whose later result page failed to load ends its line with
/// ", page N failed", since the pages after it were never read.
/// </summary>
public static class WalkPairSummaryLine
{
    private static readonly DetailPageOutcome[] DroppedReasonOrder =
    [
        DetailPageOutcome.NotMatching,
        DetailPageOutcome.PriusPrime,
        DetailPageOutcome.NewCar,
        DetailPageOutcome.Sold,
        DetailPageOutcome.NoPriceListed,
        DetailPageOutcome.MissingFields,
        DetailPageOutcome.NoVin,
        DetailPageOutcome.Repeat,
        DetailPageOutcome.Failed,
        DetailPageOutcome.ExtractionFailed,
    ];

    private const string ContinuationIndent = "    ";

    /// <summary>The per-pair line printed right after its walk. When the whole line would be wider
    /// than <paramref name="width"/> (the console width; unbounded by default), the tally stays on
    /// the first line and the parenthesized reason breakdown moves, intact, to a second line
    /// indented by four spaces, so the console never breaks it mid-parenthetical.
    /// <paramref name="skippedBeyondRadius"/> and <paramref name="skippedNoDistance"/> are cars.com's
    /// own counts (see <see cref="WalkSearchPages.CollectLinksAsync"/>): cards whose stated distance was
    /// checked and found beyond the scenario's radius, and cards that stated no distance at all, neither
    /// of which was ever a candidate, so neither is part of <paramref name="dropped"/>. Zero for a site
    /// with no radius check, so its line is unchanged. <paramref name="skippedUnrendered"/> is cars.com's
    /// own count of cards whose own text never carried a dollar amount within the bounded render wait
    /// (see <see cref="SearchPageCardRenderWait"/>): also never a candidate, and named apart from the
    /// other two, since a card that never rendered was never actually measured either way.
    /// <paramref name="skippedBelowYearFloor"/> and <paramref name="skippedOverMileageCap"/> are carmax's own
    /// counts (see <see cref="WalkSearchPages.CollectLinksAsync"/>): cards whose own stated year or mileage
    /// already failed the scenario's facets, which the site's search URL carries but does not actually honor.
    /// Neither was ever a candidate either, and both are named apart from the radius counts above, since they
    /// are a different check. Zero for a site with no such check. <paramref name="skippedOverPriceCeiling"/> is
    /// every site's own count of cards whose own stated price, plus any shipping or delivery fee the card
    /// states, came to more than the scenario's price ceiling (see <see cref="WalkSearchPages.CollectLinksAsync"/>):
    /// also never a candidate, and named last, since it is checked on every site rather than only one with a
    /// year or mileage facet.</summary>
    public static string Format(
        string site,
        string make,
        string model,
        int pages,
        int known,
        int saved,
        DroppedBreakdown dropped,
        int width = int.MaxValue,
        bool capped = false,
        int? failedPage = null,
        int skippedBeyondRadius = 0,
        int skippedNoDistance = 0,
        int skippedUnrendered = 0,
        int skippedBelowYearFloor = 0,
        int skippedOverMileageCap = 0,
        int skippedOverPriceCeiling = 0)
    {
        string cappedSuffix = (capped ? ", capped" : "") + (failedPage is int page ? $", page {page} failed" : "");
        string radiusSuffix = (skippedBeyondRadius > 0 ? $", {skippedBeyondRadius} beyond radius" : "")
            + (skippedNoDistance > 0 ? $", {skippedNoDistance} no distance stated" : "")
            + (skippedUnrendered > 0 ? $", {skippedUnrendered} unrendered" : "")
            + (skippedBelowYearFloor > 0 ? $", {skippedBelowYearFloor} below year floor" : "")
            + (skippedOverMileageCap > 0 ? $", {skippedOverMileageCap} over mileage cap" : "")
            + (skippedOverPriceCeiling > 0 ? $", {skippedOverPriceCeiling} over price ceiling" : "");
        string prefix = $"{site} / {make} {model}: {pages} pages, {known} known from cards, {saved} saved, {dropped.Total} dropped";
        string cell = DroppedCell(dropped);
        if (cell.Length == 0)
        {
            return prefix + radiusSuffix + cappedSuffix;
        }

        string breakdown = $"({cell}){radiusSuffix}{cappedSuffix}";
        return prefix.Length + 1 + breakdown.Length > width
            ? $"{prefix}\n{ContinuationIndent}{breakdown}"
            : $"{prefix} {breakdown}";
    }

    /// <summary>The "N dropped" figure for the end-of-run table's "Dropped" column, with the full
    /// reason breakdown appended; the column itself wraps this onto as many lines as it needs.</summary>
    public static string TableCell(DroppedBreakdown dropped) =>
        WithDroppedSuffix(dropped.Total.ToString(), dropped);

    private static string WithDroppedSuffix(string prefix, DroppedBreakdown dropped)
    {
        string cell = DroppedCell(dropped);
        return cell.Length == 0 ? prefix : $"{prefix} ({cell})";
    }

    /// <summary>The dropped count broken out by reason, e.g. "missing fields" when exactly one
    /// reason is non-zero, or "2 missing fields, 1 no VIN" when more than one is. Empty when
    /// nothing was dropped, so callers can decide whether to wrap it in parentheses. Reasons are
    /// named in <see cref="DroppedReasonOrder"/>, every non-zero one, always.</summary>
    public static string DroppedCell(DroppedBreakdown dropped)
    {
        List<(int Count, string Reason)> present =
        [
            .. DroppedReasonOrder
                .Select(outcome => (Count: dropped[outcome], Reason: WalkOutcomeWording.DroppedReason(outcome)))
                .Where(r => r.Count > 0)
        ];

        if (present.Count == 0)
        {
            return "";
        }

        return present.Count == 1
            ? present[0].Reason
            : string.Join(", ", present.Select(r => $"{r.Count} {r.Reason}"));
    }
}
