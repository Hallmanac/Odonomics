using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class LedgerDiffServiceTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url, string source = "auto.dev", string model = "Prius", string make = "Toyota", int year = 2020, int mileage = 40000) => new()
    {
        Vin = vin,
        Source = source,
        Url = url,
        Year = year,
        Make = make,
        Model = model,
        Trim = "LE",
        Price = price,
        Mileage = mileage,
    };

    private static Scenario DaughterScenario { get; } = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static RunEntity Run(DateTimeOffset startedAt, string sources = "auto.dev:Prius", string? zip = null, int? radiusMiles = null) =>
        new() { Command = "search", Sources = sources, StartedAt = startedAt, Zip = zip, RadiusMiles = radiusMiles };

    private static async Task<GonePostingEntry> GoneAfterTwoRunsAsync(ListingCandidate candidate, RunEntity run1, RunEntity run2, Scenario scenario)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(candidate, run1, CancellationToken.None);

        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);

        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(run2, scenario, CancellationToken.None);
        return Assert.Single(diff.Gone);
    }

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

        SearchDiff diff = await diffService.ComputeAsync(run1, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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
        await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        RunEntity run3 = Run(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run3);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, "https://cars.com/a"), run3, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run3, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(walkRun, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(priusWalk, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run3, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal("1HGCM82633A004352", drop.Vin);
        Assert.Equal(18000m, drop.PreviousPrice);
        Assert.Equal(17000m, drop.CurrentPrice);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinFirstSeenOnANewSource_IsReportedAlsoListedNotNew()
    {
        // A VIN already known through one source (auto.dev) turning up for the first time on a
        // different source (cars.com) is not "moved" (nothing on cars.com to have moved from), but
        // it is a genuinely new sighting: the operator's routine is search (auto.dev/marketcheck)
        // then walk (cars.com/carvana), so this is the walk's most common real result and has to
        // show up somewhere. It's reported under "Also listed", not "New", since the vehicle row
        // itself isn't new.
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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        AlsoListedEntry entry = Assert.Single(diff.AlsoListed);
        Assert.Equal("1HGCM82633A004352", entry.Vin);
        Assert.Equal(["cars.com"], entry.Sources);
        Assert.Equal(2020, entry.Year);
        Assert.Equal("Toyota", entry.Make);
        Assert.Equal("Prius", entry.Model);
        Assert.Equal(18000m, entry.Price);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinFirstSeenOnTwoNewSourcesInTheSameRun_IsOneAlsoListedRowNamingBoth()
    {
        // A bare `odo walk` covers cars.com and carvana together in one RunEntity. If a VIN the
        // ledger already knows (from an earlier search) is dealer-cross-listed and this walk is the
        // first to spot it on either site, both sightings are genuinely new information about where
        // the car is listed, so the one row for that VIN names both sources.
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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        AlsoListedEntry entry = Assert.Single(diff.AlsoListed);
        Assert.Equal(["cars.com", "carvana.com"], entry.Sources);
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

        SearchDiff diff = await diffService.ComputeAsync(run1, DaughterScenario, CancellationToken.None);

        NewPostingEntry added = Assert.Single(diff.New);
        Assert.Equal("1HGCM82633A004352", added.Vin);
    }

    [Fact]
    public async Task ComputeAsync_NewVinReturnedByTwoSourcesInOneRun_IsOneNewRowNamingTheFirstSourceSeen()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "auto.dev:Prius,marketcheck:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://autodev.com/a", "auto.dev"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18500m, "https://marketcheck.com/a", "marketcheck"), run1, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run1, DaughterScenario, CancellationToken.None);

        NewPostingEntry added = Assert.Single(diff.New);
        Assert.Equal("auto.dev", added.Source);
        Assert.Equal(18000m, added.Price);
        Assert.Empty(diff.AlsoListed);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinGainingTwoSourcesInOneRun_IsOneAlsoListedRowAndNoNewRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "cars.com:Prius,auto.dev:Prius,marketcheck:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17900m, "https://autodev.com/a", "auto.dev"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17900m, "https://marketcheck.com/a", "marketcheck"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        AlsoListedEntry entry = Assert.Single(diff.AlsoListed);
        Assert.Equal("1HGCM82633A004352", entry.Vin);
        Assert.Equal(["auto.dev", "marketcheck"], entry.Sources);
        Assert.Equal(17900m, entry.Price);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_KnownVinWithASecondPostingOnASourceItWasAlreadyOn_IsNotReportedAlsoListed()
    {
        // The VIN was already on marketcheck through posting a, which this run sees again; the run
        // also saves a second marketcheck posting (a different dealer). The car gained no source,
        // so nothing is reported for it: not "also listed", not "new", and not "moved" either, since
        // posting a was not left behind.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var diffService = new LedgerDiffService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "marketcheck:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://marketcheck.com/a", "marketcheck"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "marketcheck:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://marketcheck.com/a", "marketcheck"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://marketcheck.com/b", "marketcheck"), run2, CancellationToken.None);

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.AlsoListed);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

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

        SearchDiff diff = await diffService.ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Moved);
        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.Gone);
    }

    private static readonly DateTimeOffset FirstRunAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ComputeAsync_GoneVehicleBelowTheModelsMinimumYear_IsGoneForBelowYearFacet()
    {
        // The 2010 Insight from walk run 4: the scenario's Insight minimum is 2019.
        ListingCandidate insight = Candidate("JHMZE2H79AS041642", 9000m, "https://cars.com/insight", "cars.com", "Insight", "Honda", year: 2010);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            insight,
            Run(FirstRunAt, "cars.com:Insight", "32833", 50),
            Run(SecondRunAt, "cars.com:Insight", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.BelowYearFacet, gone.Reason);
        Assert.Equal("below year facet", gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_GoneVehicleUsesTheModelsOwnMinimumYear()
    {
        // The scenario allows a 2018 Camry Hybrid, so a 2018 is inside the year facet.
        ListingCandidate camry = Candidate("4T1B11HK5JU000001", 17000m, "https://cars.com/camry", "cars.com", "Camry Hybrid", "Toyota", year: 2018);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            camry,
            Run(FirstRunAt, "cars.com:Camry Hybrid", "32833", 50),
            Run(SecondRunAt, "cars.com:Camry Hybrid", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.NotOnSearchPage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_GoneVehicleOverTheMileageLimit_IsGoneForOverMileage()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000001", 9000m, "https://cars.com/prius", "cars.com", mileage: DaughterScenario.Filters.MaxMileage + 1);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.OverMileage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_ZipMovedSinceTheRunThatLastSawTheVehicle_IsGoneForSearchMoved()
    {
        // The Daytona-area Camry from walk run 4 fell outside the radius when the zip moved.
        ListingCandidate camry = Candidate("4T1DAACK9TU267793", 24000m, "https://cars.com/daytona", "cars.com", "Camry Hybrid", "Toyota", year: 2026);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            camry,
            Run(FirstRunAt, "cars.com:Camry Hybrid", "32114", 50),
            Run(SecondRunAt, "cars.com:Camry Hybrid", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.SearchMoved, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_RadiusChangedSinceTheRunThatLastSawTheVehicle_IsGoneForSearchMoved()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000002", 15000m, "https://cars.com/prius", "cars.com");

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 100),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.SearchMoved, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_SameZipAndRadiusAsTheRunThatLastSawTheVehicle_IsGoneForNotOnSearchPage()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000003", 15000m, "https://cars.com/prius", "cars.com");

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.NotOnSearchPage, gone.Reason);
        Assert.Equal("not on search page", gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PriorRunRecordedNoZipOrRadius_NeverYieldsSearchMoved()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000004", 15000m, "https://cars.com/prius", "cars.com");

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius"),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.NotOnSearchPage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PairCoveredPartiallyByThisRun_ReportsAnUntouchedPostingAsBeyondTheCap()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000006", 15000m, "https://cars.com/prius", "cars.com");

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, $"cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.BeyondTheCap, gone.Reason);
        Assert.Equal("beyond the cap", gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_AnotherPairCoveredPartially_LeavesThisPairsGoneAsNotOnSearchPage()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000007", 15000m, "https://cars.com/prius", "cars.com");

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius,carvana:Prius", "32833", 50),
            Run(SecondRunAt, $"cars.com:Prius,carvana:Prius,{RunSources.PartialKey("carvana:Prius")}", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.NotOnSearchPage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PartialPairWhoseSearchMoved_IsStillGoneForSearchMoved()
    {
        ListingCandidate camry = Candidate("4T1DAACK9TU267794", 24000m, "https://cars.com/daytona", "cars.com", "Camry Hybrid", "Toyota", year: 2026);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            camry,
            Run(FirstRunAt, "cars.com:Camry Hybrid", "32114", 50),
            Run(SecondRunAt, $"cars.com:Camry Hybrid,{RunSources.PartialKey("cars.com:Camry Hybrid")}", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.SearchMoved, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PartialPairWithAVehicleOverMileageOrBelowTheYearFacet_KeepsTheEarlierReasons()
    {
        ListingCandidate insight = Candidate("JHMZE2H79AS041645", 4000m, "https://cars.com/insight", "cars.com", "Insight", "Honda", year: 2010, mileage: 150000);
        ListingCandidate prius = Candidate("JTDKN3DU0A0000008", 4000m, "https://cars.com/prius", "cars.com", mileage: 150000);

        GonePostingEntry belowYear = await GoneAfterTwoRunsAsync(
            insight,
            Run(FirstRunAt, "cars.com:Insight", "32833", 50),
            Run(SecondRunAt, $"cars.com:Insight,{RunSources.PartialKey("cars.com:Insight")}", "32833", 50),
            DaughterScenario);
        GonePostingEntry overMileage = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, $"cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.BelowYearFacet, belowYear.Reason);
        Assert.Equal(GoneReasons.OverMileage, overMileage.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PartialPairsMarkerAlone_DoesNotCountAsCoverageOfItsOwn()
    {
        // A marker with no plain token beside it names no covered pair, so it can vouch for nothing.
        ListingCandidate prius = Candidate("JTDKN3DU0A0000009", 15000m, "https://cars.com/prius", "cars.com");
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        RunEntity run1 = Run(FirstRunAt, "cars.com:Prius", "32833", 50);
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(prius, run1, CancellationToken.None);
        RunEntity run2 = Run(SecondRunAt, RunSources.PartialKey("cars.com:Prius"), "32833", 50);
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);

        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.Gone);
    }

    private static readonly DateTimeOffset ThirdRunAt = new(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Upserts <paramref name="candidate"/> in the first run only, saves every later run with no
    /// sighting, and returns the diff of the last one.</summary>
    private static async Task<SearchDiff> DiffAfterRunsAsync(ListingCandidate candidate, params RunEntity[] runs)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        db.Runs.Add(runs[0]);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(candidate, runs[0], CancellationToken.None);
        foreach (RunEntity run in runs[1..])
        {
            db.Runs.Add(run);
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return await new LedgerDiffService(db).ComputeAsync(runs[^1], DaughterScenario, CancellationToken.None);
    }

    [Fact]
    public async Task ComputeAsync_PostingAPartialRunNeverReached_IsStillGoneOnTheNextFullRunOfThePair()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000010", 15000m, "https://cars.com/prius", "cars.com");

        SearchDiff diff = await DiffAfterRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, $"cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}", "32833", 50),
            Run(ThirdRunAt, "cars.com:Prius", "32833", 50));

        GonePostingEntry gone = Assert.Single(diff.Gone);
        Assert.Equal(GoneReasons.NotOnSearchPage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_PostingAPartialRunNeverReached_IsBeyondTheCapAgainOnALaterPartialRun()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000011", 15000m, "https://cars.com/prius", "cars.com");
        string partial = $"cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}";

        SearchDiff diff = await DiffAfterRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, partial, "32833", 50),
            Run(ThirdRunAt, partial, "32833", 50));

        Assert.Equal(GoneReasons.BeyondTheCap, Assert.Single(diff.Gone).Reason);
    }

    [Fact]
    public async Task ComputeAsync_PostingLastSeenBeforeAFullRun_IsNotReportedGoneAgainAfterALaterPartialRun()
    {
        // The full second run already reported it (LastSeen is the first run's), so the chain stops there.
        ListingCandidate prius = Candidate("JTDKN3DU0A0000012", 15000m, "https://cars.com/prius", "cars.com");

        SearchDiff diff = await DiffAfterRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32833", 50),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            Run(ThirdRunAt, $"cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}", "32833", 50));

        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ComputeAsync_VehicleBelowTheYearFacetAfterTheSearchMoved_IsStillGoneForBelowYearFacet()
    {
        ListingCandidate insight = Candidate("JHMZE2H79AS041643", 9000m, "https://cars.com/insight", "cars.com", "Insight", "Honda", year: 2010);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            insight,
            Run(FirstRunAt, "cars.com:Insight", "32114", 50),
            Run(SecondRunAt, "cars.com:Insight", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.BelowYearFacet, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_VehicleOverMileageAndBelowTheYearFacet_ReportsTheYearFacetFirst()
    {
        ListingCandidate insight = Candidate("JHMZE2H79AS041644", 4000m, "https://cars.com/insight", "cars.com", "Insight", "Honda", year: 2010, mileage: 150000);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            insight,
            Run(FirstRunAt, "cars.com:Insight", "32833", 50),
            Run(SecondRunAt, "cars.com:Insight", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.BelowYearFacet, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_VehicleOverMileageAfterTheSearchMoved_IsStillGoneForOverMileage()
    {
        ListingCandidate prius = Candidate("JTDKN3DU0A0000005", 4000m, "https://cars.com/prius", "cars.com", mileage: 150000);

        GonePostingEntry gone = await GoneAfterTwoRunsAsync(
            prius,
            Run(FirstRunAt, "cars.com:Prius", "32114", 50),
            Run(SecondRunAt, "cars.com:Prius", "32833", 50),
            DaughterScenario);

        Assert.Equal(GoneReasons.OverMileage, gone.Reason);
    }

    [Fact]
    public async Task ComputeAsync_StoredPlaceholderPriceOnANewVehicle_IsNotListedUnderNew()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("JTDBCMFE2T3156781", 0m, "https://cars.com/zero"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);

        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(run1, DaughterScenario, CancellationToken.None);

        NewPostingEntry entry = Assert.Single(diff.New);
        Assert.Equal("1HGCM82633A004352", entry.Vin);
    }

    [Fact]
    public async Task ComputeAsync_PriceFallingToAPlaceholderOrRisingFromOne_IsNeverListedUnderPriceDrops()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a"), run1, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("JTDBCMFE2T3156781", 0m, "https://cars.com/zero"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 0m, "https://cars.com/a"), run2, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("JTDBCMFE2T3156781", 15000m, "https://cars.com/zero"), run2, CancellationToken.None);

        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(run2, DaughterScenario, CancellationToken.None);

        Assert.Empty(diff.PriceDrops);
        Assert.Empty(diff.New);
    }
}
