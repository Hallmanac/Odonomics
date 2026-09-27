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

    private static ListingCandidate BarePosting(string vin, int year, string model, string trim, int mileage, decimal price, string make = "Honda") => new()
    {
        Vin = vin,
        Source = "carmax",
        Url = $"https://www.carmax.com/car/{vin}",
        Year = year,
        Make = make,
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

    [Fact]
    public async Task RunAsync_BareHybridPostingAgainstAPageTitledWithoutHybrid_MatchesOnBaseModel()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // A 2025+ Camry Hybrid's own CarMax page still titles it plainly "2025 Toyota Camry": the
        // "Hybrid" word only ever appears in the ledger's canonical model, which WalkCommand stamps
        // from the scenario's "Toyota Camry Hybrid" pair rather than from the page's own title text.
        WriteRecordedDetailPage(dataDirectory, "20260926-203954", "camry-hybrid", "detail-1.txt",
            "2025 Toyota Camry\nSE\n14k miles\n\n$30,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Camry Hybrid", StartedAt = new DateTimeOffset(2026, 9, 26, 20, 39, 54, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("4T1K61AK0RU000001", 2025, "Camry Hybrid", "SE", 14000, 30998m, make: "Toyota"), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 1, AlreadySet: 0, CouldNotMatch: 0), tally);

        PostingEntity posting = (await upsertService.CarMaxPostingsAsync(CancellationToken.None)).Single(p => p.VehicleVin == "4T1K61AK0RU000001");
        Assert.Equal("CarMax Laurel", posting.Dealer?.Name);
    }

    [Fact]
    public async Task RunAsync_MileageOverwrittenToAnExactFigureByAnotherSource_StillMatchesThePagesRoundedMileage()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        WriteRecordedDetailPage(dataDirectory, "20260926-203954", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$22,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 26, 20, 39, 54, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        // 42,318 is the exact odometer figure a later, more precise source (Marketcheck, cars.com, ...)
        // overwrote the shared vehicle row with; CarMax's own page only ever prints its own rounded
        // "42k miles".
        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000003", 2019, "Insight", "EX", 42318, 22998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 1, AlreadySet: 0, CouldNotMatch: 0), tally);
    }

    [Fact]
    public async Task RunAsync_PriceDroppedSinceThePageWasRecorded_StillMatchesOnAnEarlierObservedPrice()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // The page was recorded while the car was still $22,998; CarMax cut the price by $1,000
        // afterward, and the posting's latest observed price is now $21,998.
        WriteRecordedDetailPage(dataDirectory, "20260920-000000", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$22,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var firstRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero) };
        var priceDropRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero) };
        db.Runs.AddRange(firstRun, priceDropRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000004", 2019, "Insight", "EX", 42000, 22998m), firstRun, CancellationToken.None);
        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000004", 2019, "Insight", "EX", 42000, 21998m), priceDropRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 1, AlreadySet: 0, CouldNotMatch: 0), tally);
    }

    [Fact]
    public async Task RunAsync_TwoRecordingsInTheSameRunSharingAFingerprint_LeavesTheCandidateUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // Two different cars recorded in the very same run happen to carry the exact same fingerprint:
        // proof there are two of them, even though only one candidate needs a match today.
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-23.txt",
            "2020 Honda Insight\nTouring\n51k miles\n\n$23,998\n\nReserved at CarMax Laurel, MD\n");
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-27.txt",
            "2020 Honda Insight\nTouring\n51k miles\n\n$23,998\n\nAvailable at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000005", 2020, "Insight", "Touring", 51000, 23998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 1), tally);
    }

    [Fact]
    public async Task RunAsync_RecordingsAcrossDifferentRunsDisagreeOnStore_LeavesTheCandidateUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // Two different cars recorded on two different runs happen to share a fingerprint; the newer
        // one's page names a different store than the older one's, proof they are not the same car
        // recorded twice.
        WriteRecordedDetailPage(dataDirectory, "20260901-000000", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$23,998\n\nOnly at CarMax Laurel, MD\n");
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$23,998\n\nOnly at CarMax Norco, CA\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000006", 2019, "Insight", "EX", 42000, 23998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 1), tally);
    }

    [Fact]
    public async Task RunAsync_AnAlreadyResolvedPostingSharesACandidatesFingerprint_LeavesTheCandidateUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-1.txt",
            "2020 Honda Insight\nTouring\n51k miles\n\n$23,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        // This posting already has a real store and a reservation reading from an earlier --revisit,
        // so it is no longer a candidate for this run, but it still carries the exact same year,
        // model, trim, mileage, and price as the bare posting below: the two are still tied, and the
        // bare one must not inherit this one's page.
        var resolved = new ListingCandidate
        {
            Vin = "19XZE4F50KE000007",
            Source = "carmax",
            Url = "https://www.carmax.com/car/19XZE4F50KE000007",
            Year = 2020,
            Make = "Honda",
            Model = "Insight",
            Trim = "Touring",
            Price = 23998m,
            Mileage = 51000,
            DealerName = "CarMax North Houston",
            DealerLocation = "North Houston, TX",
            Attributes = new Dictionary<string, string> { [PostingAttributeNames.Availability] = CarMaxStores.Reserved },
        };
        await upsertService.UpsertAsync(resolved, seedRun, CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000008", 2020, "Insight", "Touring", 51000, 23998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 1, CouldNotMatch: 1), tally);
    }

    [Fact]
    public async Task RunAsync_RealDealerPostingWithNoAvailabilityAndNoMatchingRecording_CountsAsAlreadySetNotCouldNotMatch()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        // Already has a real store, and no availability attribute: the ordinary state for a car that
        // isn't reserved, not a gap this run failed to close, so it must not inflate CouldNotMatch.
        var resolved = new ListingCandidate
        {
            Vin = "19XZE4F50KE022222",
            Source = "carmax",
            Url = "https://www.carmax.com/car/2",
            Year = 2019,
            Make = "Honda",
            Model = "Insight",
            Trim = "EX",
            Price = 22998m,
            Mileage = 42000,
            DealerName = "CarMax Orlando",
            DealerLocation = "Orlando, FL",
        };
        await upsertService.UpsertAsync(resolved, seedRun, CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, seedRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 1, CouldNotMatch: 0), tally);
    }

    [Fact]
    public async Task RunAsync_BarePlainModelPostingAgainstAPageTitledAsTheHybridVariant_LeavesItUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // The gap runs only one way: a page whose own title does say "Hybrid" is unambiguous about
        // being one, so a plain, non-hybrid candidate must never be matched to it just because they'd
        // share a base model.
        WriteRecordedDetailPage(dataDirectory, "20260926-203954", "camry", "detail-1.txt",
            "2025 Toyota Camry Hybrid\nSE\n14k miles\n\n$30,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Camry", StartedAt = new DateTimeOffset(2026, 9, 26, 20, 39, 54, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("4T1K61AK0RU000002", 2025, "Camry", "SE", 14000, 30998m, make: "Toyota"), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 1), tally);
    }
}
