using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class VehicleEquipmentMigrationTests
{
    [Fact]
    public async Task Migrate_VehicleStoredBeforeTheEquipmentColumns_ReadsBackAsUnknownEquipment()
    {
        string directory = Path.Combine(Path.GetTempPath(), "odonomics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using OdonomicsDbContext db = OdonomicsDbContextFactory.CreateForDatabase(Path.Combine(directory, "odonomics.db"));
            IMigrator migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261003203703_AddAuctionChecks", CancellationToken.None);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Vehicles (Vin, Year, Make, Model, Mileage, FirstSeen, LastSeen) VALUES ('JTDBCMFE7P3014805', 2023, 'Toyota', 'Corolla Hybrid', 41200, '2026-10-01 00:00:00+00:00', '2026-10-01 00:00:00+00:00')",
                CancellationToken.None);

            await db.Database.MigrateAsync(CancellationToken.None);

            VehicleEntity vehicle = await db.Vehicles.SingleAsync(CancellationToken.None);
            Assert.Equal(VehicleEquipment.Unknown, vehicle.StoredEquipment);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
