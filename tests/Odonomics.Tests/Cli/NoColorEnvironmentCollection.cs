namespace Odonomics.Tests.Cli;

/// <summary>Forces every test that reads or mutates <c>NO_COLOR</c> onto one xUnit collection,
/// which xUnit never runs in parallel with itself. Mirrors <c>DataDirectoryEnvironmentCollection</c>
/// for the same reason: mutating a process-wide environment variable at runtime can race any other
/// test that builds a console from it mid-test.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class NoColorEnvironmentCollection
{
    public const string Name = "NO_COLOR environment";
}
