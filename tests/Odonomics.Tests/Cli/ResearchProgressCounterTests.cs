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

    private static IAnsiConsole InteractiveConsole(StringWriter writer)
    {
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });
        console.Profile.Capabilities.Interactive = true;
        return console;
    }

    [Fact]
    public void Write_MultipleCallsOnAnInteractiveConsole_OverwriteTheSameLineInsteadOfScrolling()
    {
        var writer = new StringWriter();
        IAnsiConsole console = InteractiveConsole(writer);

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
    public void Finish_OnAnInteractiveConsole_MovesPastTheCounterLineWithANewline()
    {
        var writer = new StringWriter();
        IAnsiConsole console = InteractiveConsole(writer);

        ResearchProgressCounter.Write(console, 1, 1, 1, 0, 0);
        ResearchProgressCounter.Finish(console);

        Assert.EndsWith("\n", writer.ToString());
    }

    [Fact]
    public void Write_MultipleCallsOnANonInteractiveConsole_WritesOnePlainLinePerCallWithNoCarriageReturn()
    {
        // A redirected `--quiet > research.log` run: Capabilities.Interactive is false (Spectre's own
        // redirected-output detection), so each call has to end its own line rather than overwrite one,
        // or the log would be 84 copies of the counter joined by bare carriage returns.
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

        ResearchProgressCounter.Write(console, 1, 84, 1, 0, 0);
        ResearchProgressCounter.Write(console, 2, 84, 1, 1, 0);
        ResearchProgressCounter.Write(console, 3, 84, 2, 1, 0);

        string output = writer.ToString();
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.DoesNotContain('\r', output);
        Assert.Equal(
            [
                ResearchProgressCounter.Format(1, 84, 1, 0, 0),
                ResearchProgressCounter.Format(2, 84, 1, 1, 0),
                ResearchProgressCounter.Format(3, 84, 2, 1, 0),
            ],
            lines);
    }

    [Fact]
    public void Finish_OnANonInteractiveConsole_WritesNothing()
    {
        // Every call already ended its own line, so an extra newline here would leave a blank line in
        // the log.
        var writer = new StringWriter();
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(writer) });

        ResearchProgressCounter.Write(console, 1, 1, 1, 0, 0);
        ResearchProgressCounter.Finish(console);

        Assert.Equal($"{ResearchProgressCounter.Format(1, 1, 1, 0, 0)}\n", writer.ToString());
    }
}
