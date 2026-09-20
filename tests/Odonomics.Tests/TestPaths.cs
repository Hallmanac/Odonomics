namespace Odonomics.Tests;

/// <summary>Locates repo-root fixtures (scenarios/, tests/Odonomics.Tests/fixtures/) from wherever
/// the test assembly actually runs, by walking up from the assembly's own directory to the
/// directory that holds Odonomics.sln.</summary>
public static class TestPaths
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    public static string RepoRoot => RepoRootLazy.Value;

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Odonomics.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("could not find Odonomics.sln above " + AppContext.BaseDirectory);
    }
}
