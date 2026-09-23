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

    [Fact]
    public async Task ComputeAsync_VinRelistedUnderADifferentUrl_IsNeverReportedGoneOrNew_ReportsMovedInstead()
    {
        // The exact shape of the 2026-09-22 run gap: a VIN's old posting URL goes untouched (the
        // listing moved, or the site's per-run query string changed before URL canonicalization),
        // but the same VIN is sighted again this run under a second URL. Since the VIN itself was
        // already known to the ledger, it must never show as new or as gone; it shows under Moved.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/b", "cars.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Empty(diff.Gone);
        Assert.Empty(diff.New);
        MovedPostingEntry movedEntry = Assert.Single(diff.Moved);
        Assert.Equal("1HGCM82633A004352", movedEntry.Vin);
        Assert.Equal("https://cars.com/a", movedEntry.OldUrl);
        Assert.Equal("https://cars.com/b", movedEntry.NewUrl);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinWalkedAgainUnderItsCanonicalUrlOnASecondRun_IsReportedMovedNotNew()
    {
        // The literal scenario from the task: run one sights a VIN under cars.com's raw,
        // sid-bearing detail href (before WalkSites.CanonicalDetailUrl strips it); run two sights
        // the same VIN, same source, under the canonical URL. The VIN was already in the ledger
        // after run one, so run two must never call it new.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";
        const string canonicalUrl = "https://www.cars.com/vehicledetail/abc123/";

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl, "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, canonicalUrl, "cars.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Gone);
        MovedPostingEntry movedEntry = Assert.Single(diff.Moved);
        Assert.Equal("1HGCM82633A004352", movedEntry.Vin);
        Assert.Equal(sidBearingUrl, movedEntry.OldUrl);
        Assert.Equal(canonicalUrl, movedEntry.NewUrl);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinRelistedOnTwoSourcesInTheSameRun_BothAreReportedMoved()
    {
        // Same sibling shape as the two-new-sources case: a bare `odo walk` covers cars.com and
        // carvana in one RunEntity, and if the same already-known VIN's posting URL changed on both
        // sites in that one run, both are their own "moved" sighting and neither should be dropped
        // just because the other sorts first.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius,carvana.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://carvana.com/a", "carvana.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius,carvana.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/b", "cars.com"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://carvana.com/b", "carvana.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Equal(2, diff.Moved.Count);
        Assert.Contains(diff.Moved, e => e.Source == "cars.com" && e.OldUrl == "https://cars.com/a" && e.NewUrl == "https://cars.com/b");
        Assert.Contains(diff.Moved, e => e.Source == "carvana.com" && e.OldUrl == "https://carvana.com/a" && e.NewUrl == "https://carvana.com/b");
        Assert.Empty(diff.New);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinRelistedWithALowerPriceOnTwoSourcesInTheSameRun_BothAreReportedAsPriceDrops()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius,carvana.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://carvana.com/a", "carvana.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius,carvana.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/b", "cars.com"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 16000m, "https://carvana.com/b", "carvana.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Equal(2, diff.PriceDrops.Count);
        Assert.Contains(diff.PriceDrops, e => e.Source == "cars.com" && e.PreviousPrice == 18000m && e.CurrentPrice == 17000m);
        Assert.Contains(diff.PriceDrops, e => e.Source == "carvana.com" && e.PreviousPrice == 18000m && e.CurrentPrice == 16000m);
        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinDroppedPriceOnTwoSourcesUnderUnchangedUrlsInTheSameRun_BothAreReportedAsPriceDrops()
    {
        // The ordinary price-drop path (posting untouched by URL, just a lower observation
        // appended) hit by the same VIN on two sources in one run: auto.dev and marketcheck cover
        // the same dealer inventory, so both existing postings can legitimately get a lower price
        // in the same RunEntity. Each is its own drop, not just whichever source's Id sorts first.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,marketcheck:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://marketcheck.com/a", "marketcheck"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,marketcheck:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://autodev.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 16000m, "https://marketcheck.com/a", "marketcheck"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Equal(2, diff.PriceDrops.Count);
        Assert.Contains(diff.PriceDrops, e => e.Source == "auto.dev" && e.PreviousPrice == 18000m && e.CurrentPrice == 17000m);
        Assert.Contains(diff.PriceDrops, e => e.Source == "marketcheck" && e.PreviousPrice == 18000m && e.CurrentPrice == 16000m);
        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_SameVinAndSourceDropsPriceThroughBothTheOrdinaryAndRelistPathsInOneRun_ReportsOnlyOneDrop()
    {
        // One VIN can carry two postings on the same source: an old untouched URL that just gets a
        // lower price this run (the ordinary path) and a second, newer URL that's a stale third
        // posting's relist (the relist path). Both paths used to dedup against their own independent
        // set, so this run reported the same (VIN, source) dropping price twice, disagreeing on the
        // "was" price. They must share one set so only the first-seen posting's drop is reported.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 20000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 19000m, "https://cars.com/b", "cars.com"), run2, CancellationToken.None);

        RunEntity run3 = Run(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run3);
        await db.SaveChangesAsync(CancellationToken.None);
        // /a: same URL as run1, ordinary path, price drops 20000 -> 18000.
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run3, CancellationToken.None);
        // /c: a brand-new URL, relist path against stale /b, price drops 19000 -> 17000.
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/c", "cars.com"), run3, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run3, CancellationToken.None);

        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal("cars.com", drop.Source);
        Assert.Equal(20000m, drop.PreviousPrice);
        Assert.Equal(18000m, drop.CurrentPrice);
    }

    [Fact]
    public async Task ComputeAsync_MovedPostingWhosePriceAlsoDropped_IsFoldedIntoPriceDropsNotMoved()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/b", "cars.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal("1HGCM82633A004352", drop.Vin);
        Assert.Equal(18000m, drop.PreviousPrice);
        Assert.Equal(17000m, drop.CurrentPrice);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinFirstSeenOnANewSource_IsReportedNewNotDropped()
    {
        // A VIN already known through one source (auto.dev) turning up for the first time on a
        // different source (cars.com) is not "moved" (nothing on cars.com to have moved from), but
        // it is a genuinely new sighting: the operator's routine is search (auto.dev/marketcheck)
        // then walk (cars.com/carvana), so this is the walk's most common real result and has to
        // show up somewhere. It's reported under "New" even though the vehicle row itself isn't.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        NewPostingEntry entry = Assert.Single(diff.New);
        Assert.Equal("1HGCM82633A004352", entry.Vin);
        Assert.Equal("cars.com", entry.Source);
        Assert.Equal("https://cars.com/a", entry.Url);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinFirstSeenOnTwoNewSourcesInTheSameRun_BothAreReportedNew()
    {
        // A bare `odo walk` covers cars.com and carvana together in one RunEntity. If a VIN the
        // ledger already knows (from an earlier search) is dealer-cross-listed and this walk is the
        // first to spot it on either site, both sightings are genuinely new information about where
        // the car is listed and both must be reported, not just whichever posting sorts first.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,cars.com:Prius,carvana.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://carvana.com/a", "carvana.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Equal(2, diff.New.Count);
        Assert.Contains(diff.New, e => e.Source == "cars.com" && e.Url == "https://cars.com/a");
        Assert.Contains(diff.New, e => e.Source == "carvana.com" && e.Url == "https://carvana.com/a");
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_VinReachedTheLedgerThroughTwoPostingsInOneRun_AppearsOnceUnderNew()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/b", "cars.com"), run1, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run1, CancellationToken.None);

        NewPostingEntry added = Assert.Single(diff.New);
        Assert.Equal("1HGCM82633A004352", added.Vin);
    }

    [Fact]
    public async Task ComputeAsync_VinGoneUnderTwoStalePostings_AppearsOnceUnderGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/b", "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        GonePostingEntry gone = Assert.Single(diff.Gone);
        Assert.Equal("1HGCM82633A004352", gone.Vin);
    }

    [Fact]
    public async Task ComputeAsync_AfterTheCanonicalizationMigration_SeeingTheSamePostingAgainReportsNothing()
    {
        // A VIN's posting was stored with a sid-bearing URL before URL canonicalization, then the
        // one-time ledger migration rewrote it to its canonical form. The next run that sees the
        // exact same posting (now under its canonical URL) must be quiet: zero new, zero gone,
        // zero moved, and, since the price hasn't changed, zero price drops.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";
        const string canonicalUrl = "https://www.cars.com/vehicledetail/abc123/";

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl, "cars.com"), run1, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, canonicalUrl, "cars.com"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }
}
