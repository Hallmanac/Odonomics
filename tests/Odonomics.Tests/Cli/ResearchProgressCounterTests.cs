using Odonomics.Cli;
using Spectre.Console;

namespace Odonomics.Tests.Cli;

public class ResearchProgressCounterTests
{
    [Fact]
    public void Format_ReportsCompletedTotalAndTheThreeSourceCounts()
    {
        string line = ResearchProgressCounter.Format(completed: 12, total: 84, fetched: 8, cached: 4, unreachable: 0);

        Assert.Equal("researching 12/84 (8 fetched, 4 cached, 0 unreachable)", line);
    }

    [Fact]
    public void Write_MultipleCalls_OverwriteTheSameLineInsteadOfScrolling()
    {
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

        ResearchProgressCounter.Write(console, 1, 84, 1, 0, 0);
        ResearchProgressCounter.Write(console, 2, 84, 1, 1, 0);
        ResearchProgressCounter.Write(console, 3, 84, 2, 1, 0);

        string output = writer.ToString();

        // Every update is a fresh carriage return with no newline in between, so a terminal
        // overwrites the same line three times rather than printing three lines.
        Assert.Equal(3, output.Count(c => c == '\r'));
        Assert.DoesNotContain('\n', output);
        Assert.EndsWith(ResearchProgressCounter.Format(3, 84, 2, 1, 0).PadRight(ResearchProgressCounter.LineWidth), output);
    }

    [Fact]
    public void Finish_MovesPastTheCounterLineWithANewline()
    {
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

        ResearchProgressCounter.Write(console, 1, 1, 1, 0, 0);
        ResearchProgressCounter.Finish(console);

        Assert.EndsWith("\n", writer.ToString());
    }
}
