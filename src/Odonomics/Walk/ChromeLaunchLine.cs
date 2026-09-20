using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>The exact command the operator runs by hand to start a Chrome the walk can attach
/// to: a dedicated profile directory (so it never touches the operator's everyday Chrome profile)
/// and the debugging port `odo walk` connects to over CDP.</summary>
public static class ChromeLaunchLine
{
    public const int DebugPort = 9222;

    public static string Build()
    {
        string profileDir = Path.Combine(DataDirectory.Resolve(), "chrome-profile");

        if (OperatingSystem.IsWindows())
        {
            return $"\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"/Applications/Google\\ Chrome.app/Contents/MacOS/Google\\ Chrome --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
        }

        return $"google-chrome --remote-debugging-port={DebugPort} --user-data-dir=\"{profileDir}\"";
    }
}
