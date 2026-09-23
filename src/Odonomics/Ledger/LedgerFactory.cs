using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

/// <summary>Opens the ledger at the resolved data directory, applies any pending schema
/// migrations, then applies any pending one-time data migrations (see
/// <see cref="LedgerDataMigrations"/>), per "the CLI applies pending migrations on startup."</summary>
public static class LedgerFactory
{
    public static OdonomicsDbContext Open()
    {
        string dbPath = DataDirectory.ResolveDatabasePath();
        DbContextOptions<OdonomicsDbContext> options = new DbContextOptionsBuilder<OdonomicsDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var db = new OdonomicsDbContext(options);
        db.Database.Migrate();
        LedgerDataMigrations.ApplyAll(db);
        return db;
    }
}
