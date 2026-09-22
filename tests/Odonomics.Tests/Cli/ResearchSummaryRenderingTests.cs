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
            ["unreachable"],
        ];

        return [.. Enumerable.Range(0, 52).Select(i => new ResearchSummaryEntry(
            Year: 2015 + (i % 10),
            Make: "Honda",
            Model: $"Insight Touring Hybrid CVT {i}",
            Vin: $"1HGCM8263{i:D2}A004352",
            Tags: tagShapes[i % tagShapes.Length]))];
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
}
