namespace Odonomics.Cli;

/// <summary>
/// Formats one vehicle's line in `odo research`'s end-of-run summary: a one-letter marker for
/// where its data came from this run (<c>F</c> fetched, <c>C</c> cached, <c>U</c> unreachable; see
/// <see cref="ResearchSource"/>), year/make/model, VIN, and its red flags as short tags (e.g.
/// "mileage-drop", "5-sellers"), or "none". Bounded to <see cref="MaxLineWidth"/> the same way
/// <c>WalkPairSummaryLine</c> bounds a walk pair's line: the vehicle identity always fits (it's
/// what the operator scans the summary for), and the tag list collapses to a trailing "+N" before
/// it's ever allowed to push the line past its budget, so a vehicle with many flags never
/// dominates the summary the way a full-text flag list used to.
/// </summary>
public static class ResearchSummaryLine
{
    public const int MaxLineWidth = 80;

    public static string Format(int year, string make, string model, string vin, IReadOnlyList<string> tags, ResearchSource source)
    {
        string prefix = $"{Marker(source)} {year} {make} {model}, {vin}: ";
        string tagList = TagList(tags, Math.Max(0, MaxLineWidth - prefix.Length));
        string line = $"{prefix}{tagList}";
        return Odonomics.Cli.Format.Truncate(line, MaxLineWidth);
    }

    private static string Marker(ResearchSource source) => source switch
    {
        ResearchSource.Fetched => "F",
        ResearchSource.Cached => "C",
        ResearchSource.Unreachable => "U",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "unknown research source"),
    };

    private static string TagList(IReadOnlyList<string> tags, int maxWidth)
    {
        if (tags.Count == 0)
        {
            return "none";
        }

        for (int keep = tags.Count; keep >= 1; keep--)
        {
            string named = string.Join(", ", tags.Take(keep));
            string candidate = keep == tags.Count
                ? named
                : $"{named}, +{tags.Count - keep}";
            if (candidate.Length <= maxWidth)
            {
                return candidate;
            }
        }

        return $"{tags.Count} flags";
    }
}
