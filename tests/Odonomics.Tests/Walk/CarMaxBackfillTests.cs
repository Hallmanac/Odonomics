using Odonomics.Ledger;
using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the backfill replays two of walk run 20260927-192443's own recorded CarMax detail
/// pages (one "Only at CarMax Laurel, MD", one "Reserved at CarMax North Houston, TX", both cut from
/// its insight pair) against a ledger holding the matching bare-CarMax postings, and fills both from
/// disk, with no browser and no page visit.</summary>
public class CarMaxBackfillTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private static void WriteRecordedDetailPage(string dataDirectory, string runFolder, string pair, string fileName, string text)
    {
        string pairDirectory = Path.Combine(dataDirectory, "walks", "carmax", runFolder, pair);
        Directory.CreateDirectory(pairDirectory);
        File.WriteAllText(Path.Combine(pairDirectory, fileName), text);
    }

    private static ListingCandidate BarePosting(string vin, int year, string model, string trim, int mileage, decimal price) => new()
    {
        Vin = vin,
        Source = "carmax",
        Url = $"https://www.carmax.com/car/{vin}",
        Year = year,
        Make = "Honda",
        Model = model,
        Trim = trim,
        Price = price,
        Mileage = mileage,
        DealerName = WalkSites.CarMaxDealerName,
        DealerNameIsFallback = true,
    };

    [Fact]
    public async Task RunAsync_TwoBarePostingsMatchingRecordedPages_FillsBothFromDiskWithNoPageVisit()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-4.txt", Fixture("carmax-backfill-detail-laurel.txt"));
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-22.txt", Fixture("carmax-backfill-detail-north-houston.txt"));

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE016972", 2019, "Insight", "EX", 42000, 22998m), seedRun, CancellationToken.None);
        await upsertService.UpsertAsync(BarePosting("19XZE4F55NE012808", 2022, "Insight", "EX", 65000, 21998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);
        Assert.Equal(2, candidates.Count);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 2, AlreadySet: 0, CouldNotMatch: 0), tally);

        List<PostingEntity> updated = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        PostingEntity laurel = updated.Single(p => p.VehicleVin == "19XZE4F50KE016972");
        Assert.Equal("CarMax Laurel", laurel.Dealer?.Name);
        Assert.Equal("Laurel, MD", laurel.Dealer?.Location);
        Assert.DoesNotContain(laurel.Attributes, a => a.Name == PostingAttributeNames.Availability);

        PostingEntity reserved = updated.Single(p => p.VehicleVin == "19XZE4F55NE012808");
        Assert.Equal("CarMax North Houston", reserved.Dealer?.Name);
        Assert.Equal("North Houston, TX", reserved.Dealer?.Location);
        Assert.Equal(CarMaxStores.Reserved, reserved.Attributes.Single(a => a.Name == PostingAttributeNames.Availability).Value);
    }

    [Fact]
    public async Task RunAsync_TwoCandidatesSharingTheSameFingerprint_LeavesBothUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-4.txt", Fixture("carmax-backfill-detail-laurel.txt"));

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        // Two different VINs, but the exact same year, model, trim, mileage, and price: nothing on
        // disk can tell them apart, so neither should be touched.
        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000001", 2019, "Insight", "EX", 42000, 22998m), seedRun, CancellationToken.None);
        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000002", 2019, "Insight", "EX", 42000, 22998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 2), tally);
    }

    [Fact]
    public async Task RunAsync_PostingWithNoMatchingRecording_CountsAsCouldNotMatch()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE099999", 2019, "Insight", "EX", 42000, 22998m), seedRun, CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, seedRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 1), tally);
    }

    [Fact]
    public async Task RunAsync_PostingWithARealDealerAndAvailabilityAlready_CountsAsAlreadySetWithoutTryingToMatch()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        var resolved = new ListingCandidate
        {
            Vin = "19XZE4F50KE011111",
            Source = "carmax",
            Url = "https://www.carmax.com/car/1",
            Year = 2019,
            Make = "Honda",
            Model = "Insight",
            Trim = "EX",
            Price = 22998m,
            Mileage = 42000,
            DealerName = "CarMax Orlando",
            DealerLocation = "Orlando, FL",
            Attributes = new Dictionary<string, string> { [PostingAttributeNames.Availability] = CarMaxStores.Reserved },
        };
        await upsertService.UpsertAsync(resolved, seedRun, CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, seedRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 1, CouldNotMatch: 0), tally);
    }
}
