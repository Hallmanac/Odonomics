using Odonomics.Cli.Commands;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class WalkSummaryTableRenderingTests
{
    [Fact]
    public void BuildSummaryTable_WithNoColorSet_RendersEveryLineWithinEightyColumnsAndNoAnsiCodes()
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

            List<WalkPairSummary> summaries =
            [
                new("cars.com", "Honda Insight", 9, 7, new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0), Completed: true),
                new("carvana", "Toyota Camry Hybrid", 24, 12, new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 8, Failed: 1), Completed: true),
                new("cars.com", "Toyota Prius", 0, 0, new DroppedBreakdown(0, 0, 0, 0), Completed: false),
            ];

            console.Write(WalkCommand.BuildSummaryTable(summaries));

            string output = writer.ToString();
            string[] lines = output.Replace("\r\n", "\n").Split('\n');

            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            Assert.DoesNotContain('\u001b', output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }
}
