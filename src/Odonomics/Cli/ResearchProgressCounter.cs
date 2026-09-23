using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>The single-line counter `odo research --quiet` prints in place of a line per vehicle.
/// Written straight to the console's own <see cref="IAnsiConsoleOutput.Writer"/> (bypassing
/// Spectre's markup/layout engine, which would otherwise treat a bare carriage return as wrappable
/// text). On an interactive terminal (<see cref="Capabilities.Interactive"/>) each call leads with a
/// carriage return and no trailing newline, so it overwrites the same line instead of scrolling, and
/// <see cref="Finish"/> moves the cursor past that line once the run ends. Redirected to a file or a
/// pipe, <see cref="Capabilities.Interactive"/> is false, so each call instead ends its own line:
/// `--quiet > research.log` then produces one grep-able, diff-able line per vehicle rather than 84
/// copies of the counter joined by bare carriage returns.</summary>
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
        string text = Format(completed, total, fetched, cached, unreachable);
        if (console.Profile.Capabilities.Interactive)
        {
            console.Profile.Out.Writer.Write($"\r{text.PadRight(LineWidth)}");
        }
        else
        {
            console.Profile.Out.Writer.Write($"{text}\n");
        }
    }

    public static void Finish(IAnsiConsole console)
    {
        if (console.Profile.Capabilities.Interactive)
        {
            console.Profile.Out.Writer.Write('\n');
        }
    }
}
