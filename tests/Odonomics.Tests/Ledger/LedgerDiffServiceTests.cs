using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class LedgerDiffServiceTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url, string source = "auto.dev") => new()
    {
        Vin = vin,
        Source = source,
        Url = url,
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Trim = "LE",
        Price = price,
        Mileage = 40000,
    };

    private static RunEntity Run(DateTimeOffset startedAt) => new() { Command = "search", StartedAt = startedAt };

    [Fact]
    public async Task ComputeAsync_FirstRunEver_EverythingIsNewAndNothingIsGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run1, previousRun: null, CancellationToken.None);

        Assert.Single(diff.New);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_PriceDroppedBetweenRuns_ReportsPriceDrop()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/a"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, run1, CancellationToken.None);

        Assert.Empty(diff.New);
        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal(18000m, drop.PreviousPrice);
        Assert.Equal(17000m, drop.CurrentPrice);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_PriceIncreased_IsNotReportedAsADrop()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/a"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, run1, CancellationToken.None);

        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_PostingMissingFromLatestRun_ReportsGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);

        // run2 sees nothing for this VIN (e.g. the listing was taken down).
        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, run1, CancellationToken.None);

        GonePostingEntry gone = Assert.Single(diff.Gone);
        Assert.Equal("1HGCM82633A004352", gone.Vin);
        Assert.Equal(18000m, gone.LastKnownPrice);
    }

    [Fact]
    public async Task ComputeAsync_NewVehicleOnSecondRun_IsReportedAsNewNotAsAPriceDrop()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("2T1BURHE0JC014908", 15000m, "https://carvana.com/z", "carvana"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, run1, CancellationToken.None);

        NewPostingEntry added = Assert.Single(diff.New);
        Assert.Equal("2T1BURHE0JC014908", added.Vin);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }
}
