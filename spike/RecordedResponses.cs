namespace Spike;

/// <summary>
/// Redacts API keys out of anything about to be written to spike/recorded/. Marketcheck's own
/// API was found on day one to echo the caller's api_key back inside image-proxy URLs in the
/// response body itself, so raw responses cannot be trusted to be secret-free just because the
/// request was built carefully; every write goes through this first regardless of source.
/// </summary>
public static class SecretRedactor
{
    /// <summary>
    /// Cars.com's own browser-side GraphQL API key, served to every visitor in a
    /// <c>graphql-config</c> script block on every search and detail page. Not a secret of this
    /// project, but a live-looking credential that a secret scanner flags regardless, so it is
    /// redacted the same as this project's own keys rather than left for every page-walk fixture
    /// to carry it.
    /// </summary>
    private const string CarsComPublicGraphQlKey = "5rrmnWVl1MDzPcEnDvEp3Pu101IGXEGo";

    private static readonly List<string> Secrets = [CarsComPublicGraphQlKey];

    public static void Register(params string?[] secrets)
    {
        foreach (string? secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                Secrets.Add(secret);
            }
        }
    }

    public static string Redact(string content)
    {
        foreach (var secret in Secrets)
        {
            content = content.Replace(secret, "REDACTED");
        }
        return content;
    }
}

/// <summary>Writes every raw response for a run under spike/recorded/&lt;source&gt;/&lt;run&gt;/.</summary>
public sealed class RecordedResponses(string repoRoot, string runName)
{
    public string RunName { get; } = runName;

    public string DirectoryFor(string source)
    {
        string dir = Path.Combine(repoRoot, "spike", "recorded", Slug(source), RunName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> WriteAsync(string source, string fileName, string content, CancellationToken cancellationToken)
    {
        string dir = DirectoryFor(source);
        string path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, SecretRedactor.Redact(content), cancellationToken);
        return Path.GetRelativePath(repoRoot, path);
    }

    private static string Slug(string source) => source.ToLowerInvariant().Replace(" ", "-");
}

/// <summary>
/// Tracks which day (1, 2, or 3) this invocation is, purely by counting prior run folders. No
/// arguments needed: `dotnet run --project spike` always runs "the next day". Reading the next day
/// number (<see cref="Peek"/>) is separate from persisting it (<see cref="Commit"/>): a run that
/// crashes, is cancelled, or is Ctrl-C'd before it finishes must not have consumed a day, or the
/// next invocation silently skips the day that never actually completed.
/// </summary>
public static class RunState
{
    private static string StateFile(string repoRoot) => Path.Combine(repoRoot, "spike", "recorded", "run-state.txt");

    public static (int Day, string RunName) Peek(string repoRoot)
    {
        string stateFile = StateFile(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        int day = 1;
        if (File.Exists(stateFile) && int.TryParse(File.ReadAllText(stateFile).Trim(), out int lastDay))
        {
            day = lastDay + 1;
        }

        return (day, $"day{day}");
    }

    /// <summary>Persists <paramref name="day"/> as complete. Call only once the run's results have
    /// actually been written out (findings row appended, candidate dump written); otherwise a day
    /// number is spent on a run that produced nothing.</summary>
    public static void Commit(string repoRoot, int day)
    {
        string stateFile = StateFile(repoRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        File.WriteAllText(stateFile, day.ToString());
    }
}
