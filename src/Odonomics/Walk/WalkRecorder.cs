namespace Odonomics.Walk;

/// <summary>Writes every page's text under the data directory for this run, per the brief's
/// "every page's text is recorded under the data directory for the run". Scoped by model as well
/// as site and run, since a bare walk visits several models on the same site in one run and each
/// model's own search/detail pages would otherwise collide on the same file names.</summary>
public sealed class WalkRecorder(string dataDirectory, string site, string model, DateTimeOffset runStartedAt)
{
    private string RunDirectory { get; } = Path.Combine(dataDirectory, "walks", site, runStartedAt.ToString("yyyyMMdd-HHmmss"), WalkSites.Slugify(model));

    public async Task<string> WriteAsync(string fileName, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(RunDirectory);
        string path = Path.Combine(RunDirectory, fileName);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }
}
