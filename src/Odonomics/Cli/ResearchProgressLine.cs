namespace Odonomics.Cli;

/// <summary>Formats one vehicle's line in `odo research`'s live progress output: the label plus
/// its flag kinds inline (e.g. "researched, 12-sellers, mileage-drop"), the same short tags the
/// end-of-run summary shows for that vehicle (see <see cref="ResearchSummaryLine"/>), so the
/// operator sees what tripped as soon as it's found rather than only a flag count that made them
/// wait for the summary to learn which flags they were. Printed only for a vehicle actually
/// fetched this run; a cached vehicle needs no progress line since nothing happened for it. Bounded
/// to <see cref="MaxLineWidth"/> the same way every other line-per-item formatter in this namespace
/// is (<see cref="ResearchSummaryLine"/>, <c>WalkPairSummaryLine</c>), so a vehicle with an unusually
/// long tag list never wraps this line onto a second one.</summary>
public static class ResearchProgressLine
{
    public const int MaxLineWidth = 80;

    public static string Format(string label, IReadOnlyList<string> tags)
    {
        string tagList = tags.Count == 0 ? "none" : string.Join(", ", tags);
        return Odonomics.Cli.Format.Truncate($"{label}: researched, {tagList}", MaxLineWidth);
    }
}
