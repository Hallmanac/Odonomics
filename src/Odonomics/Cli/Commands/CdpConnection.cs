using Microsoft.Playwright;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>The CDP connection behavior `odo walk` and `odo dealer grade` share: both connect to
/// the same operator-launched browser on the same debugging port, print the identical
/// launch-line message when nothing is listening, and pause for the operator on a bot-defense
/// challenge the same way.</summary>
internal static class CdpConnection
{
    public static Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        CdpAvailability.IsListeningAsync(ChromeLaunchLine.DebugPort, cancellationToken);

    public static void PrintUnavailableMessage()
    {
        AnsiConsole.MarkupLine("[yellow]nothing is listening on the CDP debugging port. Launch Chrome or Microsoft Edge with one of these lines, then run the command again. The browser stays in the background once it's running, so you can keep working in other windows while it visits pages.[/]");
        AnsiConsole.WriteLine();
        if (OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[grey]Chrome, PowerShell:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.BuildForPowerShell());
            AnsiConsole.MarkupLine("[grey]Chrome, cmd:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.Build());
            AnsiConsole.MarkupLine("[grey]Edge, PowerShell:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.BuildForEdgeForPowerShell());
            AnsiConsole.MarkupLine("[grey]Edge, cmd:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.BuildForEdge());
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]Chrome:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.Build());
            AnsiConsole.MarkupLine("[grey]Edge:[/]");
            AnsiConsole.WriteLine(ChromeLaunchLine.BuildForEdge());
        }
    }

    /// <summary>Beeps, prints the prompt, blocks on Enter, then reloads the page once. Settles for
    /// a few seconds before the first check, then a few more if it still looks blocked: a
    /// bot-defense JS challenge often resolves itself within a few seconds in a real Chrome tab,
    /// so checking the instant navigation completes risks a false positive on a page that just
    /// hasn't finished rendering yet. Bringing the challenged page to front is the one activation
    /// this is allowed to make; the operator's previously frontmost app goes back in front once
    /// they press Enter, so the browser returns to the background exactly where it left off.</summary>
    public static async Task HandleChallengeIfPresentAsync(IPage page, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        string title = await page.TitleAsync();
        string bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
        if (ChallengeDetector.IsChallenge(title, bodyText))
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            title = await page.TitleAsync();
            bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
        }

        if (!ChallengeDetector.IsChallenge(title, bodyText))
        {
            return;
        }

        string? frontmostApp = await ForegroundApp.CaptureFrontmostAsync(cancellationToken);
        await page.BringToFrontAsync();
        try
        {
            Console.Write('\a');
            AnsiConsole.MarkupLineInterpolated($"[bold red]challenge on {page.Url}: solve it in the browser, then press Enter[/]");
            await Console.In.ReadLineAsync(cancellationToken);
            await page.ReloadAsync();
        }
        finally
        {
            await ForegroundApp.RestoreAsync(frontmostApp, cancellationToken);
        }
    }
}
