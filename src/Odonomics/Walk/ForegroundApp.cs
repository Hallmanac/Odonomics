using System.Diagnostics;

namespace Odonomics.Walk;

/// <summary>Captures and restores the frontmost macOS application around the one moment the walk
/// is allowed to steal focus: a bot-defense challenge the operator has to solve by hand. Bringing
/// the challenged page to front (<see cref="Microsoft.Playwright.IPage.BringToFrontAsync"/>)
/// raises the whole browser app above whatever the operator was working in; this puts that other
/// app back in front once they press Enter. No-op on every other platform, since CDP's own
/// background-target flag is what keeps the walk out of the way there, not this.</summary>
public static class ForegroundApp
{
    public const string CaptureFrontmostScript =
        "tell application \"System Events\" to get bundle identifier of first application process whose frontmost is true";

    /// <summary>Activates by bundle identifier, passed in as <c>argv</c> rather than interpolated into
    /// the script text: a process name can contain a `"` that would otherwise break out of the
    /// AppleScript string literal, and a bundle identifier never needs escaping this way regardless.
    /// Restoring by application name also fails for apps whose System Events process name differs
    /// from the name AppleScript's `tell application` expects (VS Code's process is `Code`, for
    /// example) — the bundle identifier is the one name both sides agree on.</summary>
    public const string RestoreScript =
        "on run argv\n" +
        "\ttell application id (item 1 of argv) to activate\n" +
        "end run";

    public static async Task<string?> CaptureFrontmostAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        return await RunOsaScriptAsync(CaptureFrontmostScript, cancellationToken);
    }

    public static async Task RestoreAsync(string? frontmostBundleId, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(frontmostBundleId))
        {
            return;
        }

        await RunOsaScriptAsync(RestoreScript, cancellationToken, frontmostBundleId);
    }

    private static async Task<string?> RunOsaScriptAsync(
        string script,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("osascript") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);
            foreach (string argument in arguments)
            {
                psi.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
