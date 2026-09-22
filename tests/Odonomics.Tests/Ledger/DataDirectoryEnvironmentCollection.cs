namespace Odonomics.Tests.Ledger;

/// <summary>Forces every test that reads or mutates <c>ODO_DATA_DIR</c> onto one xUnit collection,
/// which xUnit never runs in parallel with itself. Without this, <see cref="DataDirectoryTests"/>
/// setting the variable at runtime can race any other test that resolves a path built from it
/// mid-test, such as a Chrome launch line, and produce a value from whichever call landed between
/// the set and the restore.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class DataDirectoryEnvironmentCollection
{
    public const string Name = "ODO_DATA_DIR environment";
}
