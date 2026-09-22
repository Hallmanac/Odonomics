using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

[Collection(DataDirectoryEnvironmentCollection.Name)]
public class DataDirectoryTests
{
    [Fact]
    public void Resolve_OdoDataDirSet_OverridesPlatformDefault()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "odo-data-dir-test-" + Guid.NewGuid().ToString("N"));
        string? original = Environment.GetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, tempDir);

            string resolved = DataDirectory.Resolve();

            Assert.Equal(tempDir, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, original);
        }
    }

    [Fact]
    public void ResolveDatabasePath_CreatesDirectoryAndReturnsDbFilePath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "odo-data-dir-test-" + Guid.NewGuid().ToString("N"));
        string? original = Environment.GetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, tempDir);

            string dbPath = DataDirectory.ResolveDatabasePath();

            Assert.True(Directory.Exists(tempDir));
            Assert.Equal(Path.Combine(tempDir, "odonomics.db"), dbPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, original);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
