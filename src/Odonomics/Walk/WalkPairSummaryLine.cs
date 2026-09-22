namespace Odonomics.Walk;

/// <summary>
/// Formats one (site, model) pair's tally into the single line printed right after its walk and
/// into the "Dropped" cell of the end-of-run summary table, so the two always agree with each
/// other and with the reason wording <see cref="WalkOutcomeWording"/> gives the per-page detail
/// lines above them. Saved plus every non-zero dropped reason always sums to pages, since both
/// come from the same <see cref="DroppedBreakdown"/>. The reason breakdown is bounded to whatever
/// room is actually available (the full 80 columns for <see cref="Format"/>, a table column's own
/// width for <see cref="TableCell"/>): the fixed-order reasons that fit are named, a trailing
/// "+N" accounts for however many more reasons didn't, and the breakdown is dropped entirely
/// before the dropped total itself is ever allowed to push the line past its budget.
/// </summary>
public static class WalkPairSummaryLine
{
    public const int MaxLineWidth = 80;

    private static readonly DetailPageOutcome[] DroppedReasonOrder =
    [
        DetailPageOutcome.MissingFields,
        DetailPageOutcome.NoVin,
        DetailPageOutcome.NotMatching,
        DetailPageOutcome.Repeat,
        DetailPageOutcome.Failed,
        DetailPageOutcome.ExtractionFailed,
    ];

    public static string Format(string site, string make, string model, int pages, int saved, DroppedBreakdown dropped)
    {
        string prefix = $"{site} / {make} {model}: {pages} pages, {saved} saved, {dropped.Total} dropped";
        return WithDroppedSuffix(prefix, dropped, MaxLineWidth);
    }

    /// <summary>The "N dropped" figure for the end-of-run table's "Dropped" column, with as much
    /// of the reason breakdown appended as fits in <paramref name="maxWidth"/>.</summary>
    public static string TableCell(DroppedBreakdown dropped, int maxWidth) =>
        WithDroppedSuffix(dropped.Total.ToString(), dropped, maxWidth);

    private static string WithDroppedSuffix(string prefix, DroppedBreakdown dropped, int maxWidth)
    {
        if (dropped.Total == 0)
        {
            return prefix;
        }

        int budget = Math.Max(0, maxWidth - prefix.Length - " ()".Length);
        string cell = DroppedCell(dropped, budget);
        return cell.Length == 0 ? prefix : $"{prefix} ({cell})";
    }

    /// <summary>The dropped count broken out by reason, e.g. "missing fields" when exactly one
    /// reason is non-zero, or "2 missing fields, 1 no VIN" when more than one is. Empty when
    /// nothing was dropped, so callers can decide whether to wrap it in parentheses.
    /// Reasons are named in <see cref="DroppedReasonOrder"/> up to however many fit within
    /// <paramref name="maxWidth"/>; once one doesn't fit, everything from it onward collapses
    /// into a trailing "+N", and if not even the first reason fits, the whole breakdown is
    /// omitted (the caller still has the total dropped count to show on its own).</summary>
    public static string DroppedCell(DroppedBreakdown dropped, int maxWidth = int.MaxValue)
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

        if (present.Count == 1)
        {
            return present[0].Reason.Length <= maxWidth ? present[0].Reason : "";
        }

        for (int keep = present.Count; keep >= 1; keep--)
        {
            string named = string.Join(", ", present.Take(keep).Select(r => $"{r.Count} {r.Reason}"));
            string candidate = keep == present.Count ? named : $"{named}, +{present.Count - keep}";
            if (candidate.Length <= maxWidth)
            {
                return candidate;
            }
        }

        return "";
    }
}
