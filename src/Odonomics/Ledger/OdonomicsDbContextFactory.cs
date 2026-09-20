using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Odonomics.Ledger;

/// <summary>Design-time factory so `dotnet ef migrations add` can build a context without
/// running the CLI; the connection string here is only ever used at design time.</summary>
public sealed class OdonomicsDbContextFactory : IDesignTimeDbContextFactory<OdonomicsDbContext>
{
    public OdonomicsDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<OdonomicsDbContext> options = new DbContextOptionsBuilder<OdonomicsDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new OdonomicsDbContext(options);
    }

    public static OdonomicsDbContext CreateForDatabase(string databasePath)
    {
        DbContextOptions<OdonomicsDbContext> options = new DbContextOptionsBuilder<OdonomicsDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new OdonomicsDbContext(options);
    }
}
