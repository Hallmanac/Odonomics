using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>The exact command the operator runs by hand to start a browser the walk can attach
/// to: a dedicated profile directory (so it never touches the operator's everyday browsing
/// profile) and the debugging port `odo walk` connects to over CDP. Chrome and Microsoft Edge
/// both speak the same DevTools Protocol, so either works; Edge matters in practice since it
/// ships by default on Windows and is often the only Chromium browser installed on a Mac. Each
/// browser gets its own profile directory rather than sharing one: Chrome and Edge each encrypt
/// their stored cookies with a browser-specific key (macOS Keychain entries "Chrome Safe Storage"
/// and "Microsoft Edge Safe Storage"; Windows 127+ also ties Chrome to app-bound encryption), and
/// each rewrites `Local State`, `Preferences` and the profile's SQLite databases in its own
/// schema version. Sharing a directory would make the other browser drop cookies it can't decrypt
/// (so a cleared bot-defense challenge doesn't carry over) and risks a profile one browser refuses
/// to open because it looks like it came from a newer version.</summary>
public static class ChromeLaunchLine
{
    public const int DebugPort = 9222;

    private const string ChromeProfileDirName = "chrome-profile";
    private const string EdgeProfileDirName = "edge-profile";

    public static string Build() => BuildFor(
        windowsPath: "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
        macPath: "/Applications/Google\\ Chrome.app/Contents/MacOS/Google\\ Chrome",
        linuxCommand: "google-chrome",
        profileDirName: ChromeProfileDirName);

    public static string BuildForEdge() => BuildFor(
        windowsPath: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
        macPath: "/Applications/Microsoft\\ Edge.app/Contents/MacOS/Microsoft\\ Edge",
        linuxCommand: "microsoft-edge",
        profileDirName: EdgeProfileDirName);

    private static string BuildFor(string windowsPath, string macPath, string linuxCommand, string profileDirName)
    {
        string profileDir = Path.Combine(DataDirectory.Resolve(), profileDirName);

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
