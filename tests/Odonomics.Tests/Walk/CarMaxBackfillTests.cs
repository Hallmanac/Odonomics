using System.Text.Json;
using Odonomics.Ledger;
using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the backfill replays two of walk run 20260927-192443's own recorded CarMax detail
/// pages (one "Only at CarMax Laurel, MD", one "Reserved at CarMax North Houston, TX", both cut from
/// its insight pair), anchored to their postings by their own recorded cards.json hrefs, and fills both
/// from disk, with no browser and no page visit.</summary>
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

    private static void WriteCardsJson(string dataDirectory, string runFolder, string pair, string fileName, params (string Href, string Card)[] entries)
    {
        string pairDirectory = Path.Combine(dataDirectory, "walks", "carmax", runFolder, pair);
        Directory.CreateDirectory(pairDirectory);
        string json = JsonSerializer.Serialize(entries.Select(e => new { href = e.Href, text = "", card = e.Card }));
        File.WriteAllText(Path.Combine(pairDirectory, fileName), json);
    }

    private static ListingCandidate BarePosting(string vin, int year, string model, string trim, int mileage, decimal price, string make = "Honda", string? url = null) => new()
    {
        Vin = vin,
        Source = "carmax",
        Url = url ?? $"https://www.carmax.com/car/{vin}",
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
    public async Task RunAsync_TwoBarePostingsAnchoredByTheirOwnCards_FillsBothFromDiskWithNoPageVisit()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-4.txt", Fixture("carmax-backfill-detail-laurel.txt"));
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-22.txt", Fixture("carmax-backfill-detail-north-houston.txt"));
        WriteCardsJson(dataDirectory, "20260927-192443", "insight", "cards.json",
            ("https://www.carmax.com/car/19XZE4F50KE016972", "2019 Honda Insight\nEX\n42k miles\n\n$22,998"),
            ("https://www.carmax.com/car/19XZE4F55NE012808", "2022 Honda Insight\nEX\n65k miles\n\n$21,998"));

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
    public async Task RunAsync_TwoCardsInThePairShareAFingerprintAndOnlyOneWasVisited_DoesNotAnchorEitherHrefToTheOthersPage()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // Cars A and B show up as two different cards in the very same pair folder, both "2025 Toyota
        // Camry, SE, 14K mi, $30,998" (CarMax prices in round, model-year-and-mileage-keyed steps, so
        // this really does happen). Only B was opened this run; A's own href was already known from an
        // earlier run and so was skipped. Anchoring by fingerprint alone, without checking whether more
        // than one of the pair's own cards shares it, would wrongly link A's URL to B's page too.
        WriteRecordedDetailPage(dataDirectory, "20260926-203954", "camry-hybrid", "detail-1.txt",
            "2025 Toyota Camry\nSE\n14k miles\n\n$30,998\n\nShips from CarMax Ft. Myers, FL\n");
        WriteCardsJson(dataDirectory, "20260926-203954", "camry-hybrid", "cards.json",
            ("https://www.carmax.com/car/70093365", "2025 Toyota Camry\nSE\n14k miles\n\n$30,998"),
            ("https://www.carmax.com/car/28754250", "2025 Toyota Camry\nSE\n14k miles\n\n$30,998"));

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Camry Hybrid", StartedAt = new DateTimeOffset(2026, 9, 26, 20, 39, 54, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(
            BarePosting("4T1K61AK0RU000010", 2025, "Camry", "SE", 14000, 30998m, make: "Toyota", url: "https://www.carmax.com/car/70093365"),
            seedRun,
            CancellationToken.None);
        await upsertService.UpsertAsync(
            BarePosting("4T1K61AK0RU000011", 2025, "Camry", "SE", 14000, 30998m, make: "Toyota", url: "https://www.carmax.com/car/28754250"),
            seedRun,
            CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);
        Assert.Equal(2, candidates.Count);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        // Both cards' own fingerprint is ambiguous within the pair, so neither href is anchored to the
        // Ft. Myers page, and both postings still tie by fingerprint alone: neither is touched.
        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 2), tally);

        List<PostingEntity> updated = await upsertService.CarMaxPostingsAsync(CancellationToken.None);
        Assert.All(updated, p => Assert.Equal(WalkSites.CarMaxDealerName, p.Dealer?.Name));
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
    public async Task RunAsync_LedgerMileageDriftedPastTheTolerantRoundingWindow_StillMatchesViaItsOwnCardAndUrl()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // The page itself still reads "25k miles"; a later, more exact source (auto.dev, cars.com) has
        // since overwritten the shared vehicle row to 24,294, so nothing about the posting's own
        // figures still resembles the page's. Its own URL and its card in cards.json, both recorded by
        // the very same walk that also recorded the detail page, still anchor it there regardless.
        WriteRecordedDetailPage(dataDirectory, "20260926-203954", "camry-hybrid", "detail-5.txt",
            "2021 Toyota Camry Hybrid\nXSE\n25k miles\n\n$31,998\n\nOnly at CarMax Daytona, FL\n");
        WriteCardsJson(dataDirectory, "20260926-203954", "camry-hybrid", "cards.json",
            ("https://www.carmax.com/car/70206244",
             "2021 Toyota Camry Hybrid\nXSE\n·\n25K mi\n$49 shipping·Get it by Tuesday\nEst. $522/mo\n·\n$31,998"));

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Camry Hybrid", StartedAt = new DateTimeOffset(2026, 9, 26, 20, 39, 54, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(
            BarePosting("4T1K61AK0RU000005", 2021, "Camry Hybrid", "XSE", 24294, 31998m, make: "Toyota", url: "https://www.carmax.com/car/70206244"),
            seedRun,
            CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 1, AlreadySet: 0, CouldNotMatch: 0), tally);

        PostingEntity posting = (await upsertService.CarMaxPostingsAsync(CancellationToken.None)).Single(p => p.VehicleVin == "4T1K61AK0RU000005");
        Assert.Equal("CarMax Daytona", posting.Dealer?.Name);
    }

    [Fact]
    public async Task RunAsync_TwoAnchoredRecordingsDisagreeOnAvailability_FillsFromTheNewestRatherThanDeclining()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // Both recordings carry the candidate's own canonical URL via their pair's cards.json, so
        // they're certainly the same car: the older run read it "Reserved at CarMax Laurel, MD" and the
        // newer run read the very same URL "Only at CarMax Laurel, MD". Because the URL anchor already
        // proves it's one car, this disagreement is evidence the car changed between runs, not a
        // fingerprint collision, so the newest recording should still win rather than the candidate
        // being declined as unmatched.
        WriteRecordedDetailPage(dataDirectory, "20260901-000000", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$23,998\n\nReserved at CarMax Laurel, MD\n");
        WriteCardsJson(dataDirectory, "20260901-000000", "insight", "cards.json",
            ("https://www.carmax.com/car/70206244", "2019 Honda Insight\nEX\n42k miles\n\n$23,998"));
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-1.txt",
            "2019 Honda Insight\nEX\n42k miles\n\n$23,998\n\nOnly at CarMax Laurel, MD\n");
        WriteCardsJson(dataDirectory, "20260927-192443", "insight", "cards.json",
            ("https://www.carmax.com/car/70206244", "2019 Honda Insight\nEX\n42k miles\n\n$23,998"));

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(
            BarePosting("19XZE4F50KE000010", 2019, "Insight", "EX", 42000, 23998m, url: "https://www.carmax.com/car/70206244"),
            seedRun,
            CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 1, AlreadySet: 0, CouldNotMatch: 0), tally);

        // The newest recording ("Only at") wins, not the older, disagreeing one ("Reserved at"): the
        // store still fills in, and no stale Reserved attribute is written.
        PostingEntity posting = (await upsertService.CarMaxPostingsAsync(CancellationToken.None)).Single(p => p.VehicleVin == "19XZE4F50KE000010");
        Assert.Equal("CarMax Laurel", posting.Dealer?.Name);
        Assert.DoesNotContain(posting.Attributes, a => a.Name == PostingAttributeNames.Availability);
    }

    [Fact]
    public async Task RunAsync_ARecordedPageFingerprintMatchesALookalikeCarWithNoUrlAnchor_LeavesTheCandidateUnmatched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        string dataDirectory = Path.GetDirectoryName(testDb.DatabasePath)!;

        // The recorded page is some other car's own detail page (a different pair folder, no card
        // naming this posting's URL at all), but its year, make, model, trim, mileage, and price happen
        // to read exactly like this posting's own vehicle row after a later, more exact source (see
        // lesson 84abc33f) overwrote its mileage to round right back to "51k miles". A fingerprint-only
        // fallback would have filled this posting from a stranger's page; with no fallback left, a
        // lookalike this close still leaves it bare rather than risking a wrong fill.
        WriteRecordedDetailPage(dataDirectory, "20260927-192443", "insight", "detail-1.txt",
            "2020 Honda Insight\nTouring\n51k miles\n\n$23,998\n\nOnly at CarMax Laurel, MD\n");

        var upsertService = new LedgerUpsertService(db);
        var seedRun = new RunEntity { Command = "walk", Sources = "carmax:Insight", StartedAt = new DateTimeOffset(2026, 9, 27, 19, 24, 43, TimeSpan.Zero) };
        db.Runs.Add(seedRun);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsertService.UpsertAsync(BarePosting("19XZE4F50KE000009", 2020, "Insight", "Touring", 51210, 23998m), seedRun, CancellationToken.None);

        var backfillRun = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(backfillRun);
        await db.SaveChangesAsync(CancellationToken.None);

        List<PostingEntity> candidates = await upsertService.CarMaxPostingsAsync(CancellationToken.None);

        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(dataDirectory, candidates, upsertService, backfillRun, CancellationToken.None);

        Assert.Equal(new CarMaxBackfillTally(Filled: 0, AlreadySet: 0, CouldNotMatch: 1), tally);

        PostingEntity posting = (await upsertService.CarMaxPostingsAsync(CancellationToken.None)).Single(p => p.VehicleVin == "19XZE4F50KE000009");
        Assert.Equal(WalkSites.CarMaxDealerName, posting.Dealer?.Name);
    }
}
