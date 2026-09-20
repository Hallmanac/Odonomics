namespace Spike;

/// <summary>
/// Redacts API keys out of anything about to be written to spike/recorded/. Marketcheck's own
/// API was found on day one to echo the caller's api_key back inside image-proxy URLs in the
/// response body itself, so raw responses cannot be trusted to be secret-free just because the
/// request was built carefully; every write goes through this first regardless of source.
/// </summary>
public static class SecretRedactor
{
    private static readonly List<string> Secrets = [];

    public static void Register(params string?[] secrets)
    {
        foreach (var secret in secrets)
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
        var dir = Path.Combine(repoRoot, "spike", "recorded", Slug(source), RunName);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<string> WriteAsync(string source, string fileName, string content, CancellationToken cancellationToken)
    {
        var dir = DirectoryFor(source);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllTextAsync(path, SecretRedactor.Redact(content), cancellationToken);
        return Path.GetRelativePath(repoRoot, path);
    }

    private static string Slug(string source) => source.ToLowerInvariant().Replace(" ", "-");
}

/// <summary>
/// Tracks which day (1, 2, or 3) this invocation is, purely by counting prior run folders. No
/// arguments needed: `dotnet run --project spike` always runs "the next day".
/// </summary>
public static class RunState
{
    public static (int Day, string RunName) NextRun(string repoRoot)
    {
        var recordedDir = Path.Combine(repoRoot, "spike", "recorded");
        var stateFile = Path.Combine(recordedDir, "run-state.txt");
        Directory.CreateDirectory(recordedDir);

        var day = 1;
        if (File.Exists(stateFile) && int.TryParse(File.ReadAllText(stateFile).Trim(), out var lastDay))
        {
            day = lastDay + 1;
        }

        File.WriteAllText(stateFile, day.ToString());
        return (day, $"day{day}");
    }
}
