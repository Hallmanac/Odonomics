using Odonomics.Cli;
using Spectre.Console;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class ResearchSummaryRenderingTests
{
    private static List<ResearchSummaryEntry> FiftyTwoVehicles()
    {
        string[][] tagShapes =
        [
            [],
            ["mileage-drop"],
            ["5-sellers"],
            ["no-remedy-recall"],
            ["mileage-drop", "3-sellers"],
        ];

        // Cycles through fetched and cached so the header's counts and the per-line marker both
        // have a mix to prove against, plus a couple of unreachable vehicles at the end (see the
        // 74-vehicle live run this replaces: 40 fetched, 44 cached, some batches carry unreachable
        // too).
        ResearchSource[] sources = [ResearchSource.Fetched, ResearchSource.Cached];

        List<ResearchSummaryEntry> entries = [.. Enumerable.Range(0, 50).Select(i => new ResearchSummaryEntry(
            Year: 2015 + (i % 10),
            Make: "Honda",
            Model: $"Insight Touring Hybrid CVT {i}",
            Vin: $"1HGCM8263{i:D2}A004352",
            Tags: tagShapes[i % tagShapes.Length],
            Source: sources[i % sources.Length]))];

        entries.Add(new ResearchSummaryEntry(2021, "Honda", "Insight Touring Hybrid CVT 50", "1HGCM826350A004352", ["unreachable"], ResearchSource.Unreachable));
        entries.Add(new ResearchSummaryEntry(2021, "Honda", "Insight Touring Hybrid CVT 51", "1HGCM826351A004352", ["unreachable"], ResearchSource.Unreachable));

        return entries;
    }

    [Fact]
    public void Render_FiftyTwoVehiclesWithNoColorSet_EveryLineFitsEightyColumnsAndFitsAboutSixtyLinesTotal()
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

            ResearchSummaryRenderer.Render(console, FiftyTwoVehicles());

            string output = writer.ToString();
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            string[] contentLines = [.. lines.Where(line => !string.IsNullOrWhiteSpace(line))];

            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            Assert.DoesNotContain('\u001b', output);

            // The header states the totals: 52 vehicles, 25 fetched, 25 cached, 2 unreachable.
            Assert.Equal("52 vehicles, 25 fetched, 25 cached, 2 unreachable", contentLines[0]);

            // One header line plus one line per vehicle: comfortably inside "about 60 lines" for 52
            // vehicles, unlike the old per-flag full-text listing this replaces.
            Assert.Equal(53, contentLines.Length);
            Assert.True(contentLines.Length <= 60, $"summary ran to {contentLines.Length} lines, expected about 60 or fewer");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    [Fact]
    public void Render_MixOfFetchedAndCached_MarksEachLineWithItsSource()
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var entries = new List<ResearchSummaryEntry>
            {
                new(2020, "Toyota", "Camry Hybrid", "4T1G11AK0LU123456", ["mileage-drop"], ResearchSource.Fetched),
                new(2019, "Honda", "Insight", "1HGCM82633A004352", [], ResearchSource.Cached),
                new(2018, "Ford", "Focus", "1FADP3F20JL123456", ["unreachable"], ResearchSource.Unreachable),
            };

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

            ResearchSummaryRenderer.Render(console, entries);

            string[] lines = writer.ToString().Replace("\r\n", "\n").Split('\n');

            Assert.Equal("3 vehicles, 1 fetched, 1 cached, 1 unreachable", lines[0]);
            Assert.StartsWith("F 2020 Toyota Camry Hybrid", lines[1]);
            Assert.StartsWith("C 2019 Honda Insight", lines[2]);
            Assert.StartsWith("U 2018 Ford Focus", lines[3]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }
}
