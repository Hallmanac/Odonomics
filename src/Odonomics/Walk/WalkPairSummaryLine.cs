namespace Odonomics.Walk;

/// <summary>
/// Formats one (site, model) pair's tally into the single line printed right after its walk and
/// into the "Dropped" cell of the end-of-run summary table, so the two always agree with each
/// other and with the reason wording <see cref="WalkOutcomeWording"/> gives the per-page detail
/// lines above them. Saved plus every non-zero dropped reason always sums to pages, since both
/// come from the same <see cref="DroppedBreakdown"/>.
/// </summary>
public static class WalkPairSummaryLine
{
    private static readonly DetailPageOutcome[] DroppedReasonOrder =
    [
        DetailPageOutcome.MissingFields,
        DetailPageOutcome.NoVin,
        DetailPageOutcome.NotMatching,
        DetailPageOutcome.Failed,
    ];

    public static string Format(string site, string make, string model, int pages, int saved, DroppedBreakdown dropped)
    {
        string cell = DroppedCell(dropped);
        return $"{site} / {make} {model}: {pages} pages, {saved} saved, {dropped.Total} dropped{(cell.Length == 0 ? "" : $" ({cell})")}";
    }

    /// <summary>The dropped count broken out by reason, e.g. "missing fields" when exactly one
    /// reason is non-zero, or "2 missing fields, 1 no VIN" when more than one is. Empty when
    /// nothing was dropped, so callers can decide whether to wrap it in parentheses.</summary>
    public static string DroppedCell(DroppedBreakdown dropped)
    {
        List<(int Count, string Reason)> present =
        [
            .. DroppedReasonOrder
                .Select(outcome => (Count: dropped[outcome], Reason: WalkOutcomeWording.DroppedReason(outcome)))
                .Where(r => r.Count > 0)
        ];

        return present.Count switch
        {
            0 => "",
            1 => present[0].Reason,
            _ => string.Join(", ", present.Select(r => $"{r.Count} {r.Reason}")),
        };
    }
}
