using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>The single self-overwriting counter line `odo research --quiet` prints in place of a
/// line per vehicle. Written straight to the console's own <see cref="IAnsiConsoleOutput.Writer"/>
/// (bypassing Spectre's markup/layout engine, which would otherwise treat a bare carriage return as
/// wrappable text) with a leading carriage return and no trailing newline, so each call overwrites
/// the same terminal line instead of scrolling. <see cref="Finish"/> moves the cursor past that
/// line once the run ends, so the summary that follows starts on its own line.</summary>
public static class ResearchProgressCounter
{
    public const int LineWidth = 79;

    public static string Format(int completed, int total, int fetched, int cached, int unreachable)
    {
        string text = $"researching {completed}/{total} ({fetched} fetched, {cached} cached, {unreachable} unreachable)";
        return text.Length > LineWidth ? text[..LineWidth] : text;
    }

    public static void Write(IAnsiConsole console, int completed, int total, int fetched, int cached, int unreachable)
    {
        string line = Format(completed, total, fetched, cached, unreachable).PadRight(LineWidth);
        console.Profile.Out.Writer.Write($"\r{line}");
    }

    public static void Finish(IAnsiConsole console) => console.Profile.Out.Writer.Write('\n');
}
