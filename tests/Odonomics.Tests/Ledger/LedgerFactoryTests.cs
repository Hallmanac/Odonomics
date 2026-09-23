using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

[Collection(DataDirectoryEnvironmentCollection.Name)]
public class LedgerFactoryTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url) => new()
    {
        Vin = vin,
        Source = "cars.com",
        Url = url,
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Trim = "LE",
        Price = price,
        Mileage = 40000,
    };

    [Fact]
    public async Task Open_AppliesThePendingCanonicalizationMigrationOnce_AndSkipsItOnEveryLaterOpen()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "odo-ledger-factory-test-" + Guid.NewGuid().ToString("N"));
        string? original = Environment.GetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(DataDirectory.OverrideEnvironmentVariable, tempDir);

            const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";
            const string canonicalUrl = "https://www.cars.com/vehicledetail/abc123/";

            // Seed a ledger file that looks like one written before URL canonicalization existed:
            // schema migrated, but no LedgerFactory.Open() has ever run against it, so its cars.com
            // posting still carries a raw sid-bearing URL.
            string dbPath = DataDirectory.ResolveDatabasePath();
            using (OdonomicsDbContext seedDb = OdonomicsDbContextFactory.CreateForDatabase(dbPath))
            {
                await seedDb.Database.MigrateAsync(CancellationToken.None);
                var run = new RunEntity { Command = "walk cars.com", Sources = "cars.com:Prius", StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
                seedDb.Runs.Add(run);
                await seedDb.SaveChangesAsync(CancellationToken.None);
                await new LedgerUpsertService(seedDb).UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl), run, CancellationToken.None);
            }

            using (OdonomicsDbContext db = LedgerFactory.Open())
            {
                PostingEntity posting = Assert.Single(db.Postings.Where(p => p.VehicleVin == "1HGCM82633A004352"));
                Assert.Equal(canonicalUrl, posting.Url);
                Assert.Single(db.LedgerMigrations, m => m.Name == "CanonicalizeCarsComPostingUrls");

                // A row that would need canonicalizing, added after the migration already ran and
                // recorded itself: proves the next Open() skips it entirely rather than rescanning.
                var laterRun = new RunEntity { Command = "walk cars.com", Sources = "cars.com:Prius", StartedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) };
                db.Runs.Add(laterRun);
                await db.SaveChangesAsync(CancellationToken.None);
                await new LedgerUpsertService(db).UpsertAsync(Candidate("2T1BURHE0JC014908", 15000m, "https://www.cars.com/vehicledetail/def456/?sid=abc"), laterRun, CancellationToken.None);
            }

            using (OdonomicsDbContext db = LedgerFactory.Open())
            {
                PostingEntity untouched = Assert.Single(db.Postings.Where(p => p.VehicleVin == "2T1BURHE0JC014908"));
                Assert.Equal("https://www.cars.com/vehicledetail/def456/?sid=abc", untouched.Url);
            }
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
