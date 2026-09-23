namespace Odonomics.Walk;

/// <summary>
/// Formats one (site, model) pair's tally into the single line printed right after its walk and
/// into the "Dropped" cell of the end-of-run summary table, so the two always agree with each
/// other and with the reason wording <see cref="WalkOutcomeWording"/> gives the per-page detail
/// lines above them. Saved plus every non-zero dropped reason always sums to pages, since both
/// come from the same <see cref="DroppedBreakdown"/>. Every non-zero reason is always named, in
/// the fixed order <see cref="DroppedReasonOrder"/> gives: the table's own column wraps a long
/// breakdown onto a continuation line under the same row rather than ever falling back to a bare
/// count, since a bare count with no reason is exactly the gap this class exists to close.
/// </summary>
public static class WalkPairSummaryLine
{
    private static readonly DetailPageOutcome[] DroppedReasonOrder =
    [
        DetailPageOutcome.NotMatching,
        DetailPageOutcome.MissingFields,
        DetailPageOutcome.NoVin,
        DetailPageOutcome.Repeat,
        DetailPageOutcome.Failed,
        DetailPageOutcome.ExtractionFailed,
    ];

    public static string Format(string site, string make, string model, int pages, int saved, DroppedBreakdown dropped)
    {
        string prefix = $"{site} / {make} {model}: {pages} pages, {saved} saved, {dropped.Total} dropped";
        return WithDroppedSuffix(prefix, dropped);
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
