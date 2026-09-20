namespace Odonomics.Walk;

/// <summary>Writes every page's text under the data directory for this run, per the brief's
/// "every page's text is recorded under the data directory for the run".</summary>
public sealed class WalkRecorder(string dataDirectory, string site, DateTimeOffset runStartedAt)
{
    private string RunDirectory { get; } = Path.Combine(dataDirectory, "walks", site, runStartedAt.ToString("yyyyMMdd-HHmmss"));

    public async Task<string> WriteAsync(string fileName, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RunDirectory);
        string path = Path.Combine(RunDirectory, fileName);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }
}
