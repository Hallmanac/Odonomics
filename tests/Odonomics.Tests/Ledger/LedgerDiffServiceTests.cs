using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class LedgerDiffServiceTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url, string source = "auto.dev", string model = "Prius") => new()
    {
        Vin = vin,
        Source = source,
        Url = url,
        Year = 2020,
        Make = "Toyota",
        Model = model,
        Trim = "LE",
        Price = price,
        Mileage = 40000,
    };

    private static RunEntity Run(DateTimeOffset startedAt, string sources = "auto.dev:Prius") => new() { Command = "search", Sources = sources, StartedAt = startedAt };

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

        SearchDiff diff = await diffService.ComputeAsync(run1, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_PriceUnchangedOnThirdRun_DoesNotReReportTheOlderDrop()
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
        await diffService.ComputeAsync(run2, CancellationToken.None);

        RunEntity run3 = Run(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run3);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/a"), run3, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run3, CancellationToken.None);

        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
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

        // run2 sees nothing for this VIN (e.g. the listing was taken down), but still covers the
        // same source, so the absence is meaningful.
        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

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

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,carvana:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("2T1BURHE0JC014908", 15000m, "https://carvana.com/z", "carvana"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        NewPostingEntry added = Assert.Single(diff.New);
        Assert.Equal("2T1BURHE0JC014908", added.Vin);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_RunCoveringOnlyOneSource_DoesNotReportOtherSourcesPostingsGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity searchRun = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,marketcheck:Prius");
        db.Runs.Add(searchRun);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), searchRun, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("2T1BURHE0JC014908", 15000m, "https://marketcheck.com/z", "marketcheck"), searchRun, CancellationToken.None);

        // A walk only ever touches cars.com; it must not report the search's auto.dev/marketcheck
        // postings as gone, since it never looked at those sources at all.
        RunEntity walkRun = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(walkRun);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("3VWFE21C04M000000", 12000m, "https://cars.com/b", "cars.com"), walkRun, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(walkRun, CancellationToken.None);

        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_WalkOfADifferentModelOnTheSameSite_DoesNotReportTheOtherModelGone()
    {
        // odo walk cars.com (Honda Insight, the scenario's default model), then
        // odo walk cars.com --model "Toyota Prius": the Prius walk must not report the earlier
        // Insight posting gone, since it never looked at cars.com Insight listings at all.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity insightWalk = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Insight");
        db.Runs.Add(insightWalk);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("19XZE4F52ME000999", 19000m, "https://cars.com/insight-a", "cars.com", "Insight"), insightWalk, CancellationToken.None);

        RunEntity priusWalk = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(priusWalk);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/prius-a", "cars.com", "Prius"), priusWalk, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(priusWalk, CancellationToken.None);

        Assert.Empty(diff.Gone);
    }
}
