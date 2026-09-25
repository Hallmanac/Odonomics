using Odonomics.Cli.Commands;
using Odonomics.Walk;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class WalkSummaryTableRenderingTests
{
    private static readonly List<WalkPairSummary> Summaries =
    [
        new("cars.com", "Honda Insight", 9, 7, new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0), Completed: true),
        new("carvana", "Toyota Camry Hybrid", 24, 12, new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 8, Failed: 1), Completed: true),
        new("cars.com", "Toyota Corolla Hybrid", 22, 10, new DroppedBreakdown(MissingFields: 2, NoVin: 1, NotMatching: 8, Failed: 1), Completed: true),
        new("cars.com", "Toyota Prius", 0, 0, new DroppedBreakdown(0, 0, 0, 0), Completed: false),
    ];

    [Fact]
    public void BuildSummaryTable_WithNoColorSet_RendersEveryLineWithinEightyColumnsAndNoAnsiCodes()
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            var writer = new StringWriter();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

            console.Write(WalkCommand.BuildSummaryTable(Summaries));

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

    [Fact]
    public void BuildSummaryTable_SingleReasonPair_NamesTheReasonInTheDroppedColumnRatherThanABareCount()
    {
        List<WalkPairSummary> summaries =
        [
            new("cars.com", "Honda Insight", 9, 7, new DroppedBreakdown(MissingFields: 2, NoVin: 0, NotMatching: 0, Failed: 0), Completed: true),
        ];

        string[] lines = Render(summaries);

        Assert.Equal(
            [
                "  Site       │ Model                  │ Pages │ Saved │ Dropped       │ Status  ",
                " ────────────┼────────────────────────┼───────┼───────┼───────────────┼──────── ",
                "  cars.com   │ Honda Insight          │ 9     │ 7     │ 2 (missing    │ ok      ",
                "             │                        │       │       │ fields)       │         ",
            ],
            lines[1..5]);
    }

    [Fact]
    public void BuildSummaryTable_MixedReasonPair_NamesEachReasonWithItsCountInTheFixedOrder()
    {
        List<WalkPairSummary> summaries =
        [
            new("cars.com", "Honda Insight", 14, 7, new DroppedBreakdown(MissingFields: 3, NoVin: 0, NotMatching: 4, Failed: 0), Completed: true),
        ];

        string[] lines = Render(summaries);

        Assert.Equal(
            [
                "  Site       │ Model                  │ Pages │ Saved │ Dropped       │ Status  ",
                " ────────────┼────────────────────────┼───────┼───────┼───────────────┼──────── ",
                "  cars.com   │ Honda Insight          │ 14    │ 7     │ 7 (4 wrong    │ ok      ",
                "             │                        │       │       │ model, 3      │         ",
                "             │                        │       │       │ missing       │         ",
                "             │                        │       │       │ fields)       │         ",
            ],
            lines[1..7]);
    }

    [Fact]
    public void BuildSummaryTable_LongestRealisticRow_WrapsReasonsOntoContinuationLinesWithoutMidWordBreaksWithinEightyColumns()
    {
        List<WalkPairSummary> summaries =
        [
            new(
                "auto trader",
                "Corolla Hybrid Limited",
                30,
                5,
                new DroppedBreakdown(MissingFields: 4, NoVin: 3, NotMatching: 8, Failed: 2, ExtractionFailed: 0, Repeat: 8),
                Completed: true),
        ];

        string[] lines = Render(summaries);

        Assert.Equal(
            [
                "  Site       │ Model                  │ Pages │ Saved │ Dropped       │ Status  ",
                " ────────────┼────────────────────────┼───────┼───────┼───────────────┼──────── ",
                "  auto trad… │ Corolla Hybrid Limited │ 30    │ 5     │ 25 (8 wrong   │ ok      ",
                "             │                        │       │       │ model, 4      │         ",
                "             │                        │       │       │ missing       │         ",
                "             │                        │       │       │ fields, 3 no  │         ",
                "             │                        │       │       │ VIN, 8        │         ",
                "             │                        │       │       │ repeat, 2     │         ",
                "             │                        │       │       │ failed to     │         ",
                "             │                        │       │       │ load)         │         ",
            ],
            lines[1..11]);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    [Fact]
    public void BuildSummaryTable_AutotraderRow_NamesTheSiteInFullRatherThanTruncatingIt()
    {
        List<WalkPairSummary> summaries =
        [
            new("autotrader", "Toyota Corolla Hybrid", 30, 5, new DroppedBreakdown(MissingFields: 4, NoVin: 3, NotMatching: 8, Failed: 2, ExtractionFailed: 0, Repeat: 8), Completed: true),
        ];

        string[] lines = Render(summaries);

        Assert.Equal("  autotrader │ Toyota Corolla Hybrid  │ 30    │ 5     │ 25 (8 wrong   │ ok      ", lines[3]);
        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
    }

    private static string[] Render(List<WalkPairSummary> summaries)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;

        console.Write(WalkCommand.BuildSummaryTable(summaries));

        return console.Output.Replace("\r\n", "\n").Split('\n');
    }
}
