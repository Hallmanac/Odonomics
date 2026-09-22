using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>One vehicle's line in `odo research`'s end-of-run summary, kept independent of the EF
/// Core entities the same way <c>VinHistoryPoint</c> is kept independent of the Marketcheck client
/// types, so a rendering test can build one directly without a database.</summary>
public sealed record ResearchSummaryEntry(int Year, string Make, string Model, string Vin, IReadOnlyList<string> Tags);

/// <summary>Prints `odo research`'s end-of-run summary: one bounded line per vehicle (see
/// <see cref="ResearchSummaryLine"/>), so a batch of dozens of vehicles reads as a compact scan
/// rather than a wall of full-text flag detail. Detail for any one vehicle belongs to `odo show`,
/// not here. Takes an explicit <see cref="IAnsiConsole"/> (rather than writing through the static
/// <c>AnsiConsole</c>) so a rendering test can capture the output the same way
/// <c>WalkCommand.BuildSummaryTable</c>'s tests do.</summary>
public static class ResearchSummaryRenderer
{
    public static void Render(IAnsiConsole console, IReadOnlyList<ResearchSummaryEntry> entries)
    {
        console.WriteLine($"Summary ({entries.Count})");
        foreach (ResearchSummaryEntry entry in entries)
        {
            string line = ResearchSummaryLine.Format(entry.Year, entry.Make, entry.Model, entry.Vin, entry.Tags);
            console.WriteLine(line);
        }
    }
}
