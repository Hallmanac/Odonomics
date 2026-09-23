using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Where a summary entry's research data came from this run: fetched fresh, served from
/// the seven-day cache without a network call, or attempted and unreachable. Drives both the
/// summary line's marker (see <see cref="ResearchSummaryLine"/>) and the header's counts.</summary>
public enum ResearchSource
{
    Fetched,
    Cached,
    Unreachable,
}

/// <summary>One vehicle's line in `odo research`'s end-of-run summary, kept independent of the EF
/// Core entities the same way <c>VinHistoryPoint</c> is kept independent of the Marketcheck client
/// types, so a rendering test can build one directly without a database.</summary>
public sealed record ResearchSummaryEntry(int Year, string Make, string Model, string Vin, IReadOnlyList<string> Tags, ResearchSource Source);

/// <summary>Prints `odo research`'s end-of-run summary: a header with the total and the
/// fetched/cached/unreachable breakdown, then one bounded line per vehicle that passed the
/// scenario's filters (see <see cref="ResearchSummaryLine"/>) regardless of whether it was fetched
/// this run or served from cache, so the summary is the one place the whole filtered set's flags
/// are visible together. Detail for any one vehicle belongs to `odo show`, not here. Takes an
/// explicit <see cref="IAnsiConsole"/> (rather than writing through the static <c>AnsiConsole</c>)
/// so a rendering test can capture the output the same way <c>WalkCommand.BuildSummaryTable</c>'s
/// tests do.</summary>
public static class ResearchSummaryRenderer
{
    public static void Render(IAnsiConsole console, IReadOnlyList<ResearchSummaryEntry> entries)
    {
        int fetched = entries.Count(e => e.Source == ResearchSource.Fetched);
        int cached = entries.Count(e => e.Source == ResearchSource.Cached);
        int unreachable = entries.Count(e => e.Source == ResearchSource.Unreachable);
        console.WriteLine($"{entries.Count} vehicles, {fetched} fetched, {cached} cached, {unreachable} unreachable");
        foreach (ResearchSummaryEntry entry in entries)
        {
            string line = ResearchSummaryLine.Format(entry.Year, entry.Make, entry.Model, entry.Vin, entry.Tags, entry.Source);
            console.WriteLine(line);
        }
    }
}
