namespace Odonomics.Ledger;

/// <summary>
/// Resolves where the SQLite ledger file lives. <c>ODO_DATA_DIR</c> always wins when set (tests
/// always set it to a temp directory); otherwise the platform's own per-user data location:
/// <c>%LOCALAPPDATA%\Odonomics</c> on Windows, <c>~/Library/Application Support/Odonomics</c> on
/// macOS, <c>$XDG_DATA_HOME/odonomics</c> (falling back to <c>~/.local/share/odonomics</c>) on
/// Linux. macOS is resolved explicitly rather than through
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/>, which returns the XDG path on
/// macOS, not Application Support.
/// </summary>
public static class DataDirectory
{
    public const string OverrideEnvironmentVariable = "ODO_DATA_DIR";

    public static string Resolve()
    {
        string? overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        if (OperatingSystem.IsWindows())
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Odonomics");
        }

        if (OperatingSystem.IsMacOS())
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Odonomics");
        }

        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
        {
            return Path.Combine(xdgDataHome, "odonomics");
        }

        string fallbackHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(fallbackHome, ".local", "share", "odonomics");
    }

    public static string ResolveDatabasePath()
    {
        string dir = Resolve();
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "odonomics.db");
    }
}
