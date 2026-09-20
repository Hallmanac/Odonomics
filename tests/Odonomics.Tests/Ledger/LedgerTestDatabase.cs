using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

/// <summary>A fresh, migrated SQLite database file under a temp directory, torn down after the
/// test. Mirrors ODO_DATA_DIR pointing at a temp directory, the same as a real run would use.</summary>
public sealed class LedgerTestDatabase : IDisposable
{
    private readonly string _directory;

    public LedgerTestDatabase()
    {
        _directory = Path.Combine(Path.GetTempPath(), "odonomics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        string dbPath = Path.Combine(_directory, "odonomics.db");

        using OdonomicsDbContext migrationContext = CreateContext(dbPath);
        migrationContext.Database.Migrate();

        DatabasePath = dbPath;
    }

    public string DatabasePath { get; }

    public OdonomicsDbContext CreateContext() => CreateContext(DatabasePath);

    private static OdonomicsDbContext CreateContext(string dbPath)
    {
        DbContextOptions<OdonomicsDbContext> options = new DbContextOptionsBuilder<OdonomicsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new OdonomicsDbContext(options);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup; a lingering SQLite file handle on some platforms is not worth
            // failing the test over.
        }
    }
}
