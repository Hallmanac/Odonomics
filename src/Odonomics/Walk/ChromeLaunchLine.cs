using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>The exact command the operator runs by hand to start a browser the walk can attach
/// to: a dedicated profile directory (so it never touches the operator's everyday browsing
/// profile) and the debugging port `odo walk` connects to over CDP. Chrome and Microsoft Edge
/// both speak the same DevTools Protocol, so either works; Edge matters in practice since it
/// ships by default on Windows and is often the only Chromium browser installed on a Mac. Both
/// browsers share the same profile directory rather than one each, so a profile that has already
/// worked through a site's bot-defense challenge in one browser stays warmed up when the operator
/// switches to the other.</summary>
public static class ChromeLaunchLine
{
    public const int DebugPort = 9222;

    private const string ProfileDirName = "chrome-profile";

    public static string Build() => BuildFor(
        windowsPath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
        macPath: "/Applications/Google\\ Chrome.app/Contents/MacOS/Google\\ Chrome",
        linuxCommand: "google-chrome");

    public static string BuildForEdge() => BuildFor(
        windowsPath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
        macPath: "/Applications/Microsoft\\ Edge.app/Contents/MacOS/Microsoft\\ Edge",
        linuxCommand: "microsoft-edge");

    private static string BuildFor(string windowsPath, string macPath, string linuxCommand)
    {
        string profileDir = Path.Combine(DataDirectory.Resolve(), ProfileDirName);

        if (OperatingSystem.IsWindows())
        {
            return $"\"{windowsPath}\" --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"{macPath} --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
        }

        return $"{linuxCommand} --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
    }

    /// <summary>The Windows line from <see cref="Build"/>, prefixed with PowerShell's call
    /// operator. A bare quoted path followed by arguments (what <see cref="Build"/> returns) runs
    /// fine in cmd, but PowerShell only invokes a leading quoted string as a command when told to
    /// with <c>&amp;</c>; without it, PowerShell parses the rest of the line as further expression
    /// tokens and fails with "Unexpected token". Only meaningful on Windows.</summary>
    public static string BuildForPowerShell() => $"& {Build()}";

    /// <summary>The Edge equivalent of <see cref="BuildForPowerShell"/>.</summary>
    public static string BuildForEdgeForPowerShell() => $"& {BuildForEdge()}";
}
