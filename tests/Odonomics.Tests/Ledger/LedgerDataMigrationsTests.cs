using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class LedgerDataMigrationsTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url, string source = "cars.com") => new()
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

    private static RunEntity Run(DateTimeOffset startedAt, string sources = "cars.com:Prius") => new() { Command = "walk cars.com", Sources = sources, StartedAt = startedAt };

    [Fact]
    public async Task ApplyAll_TwoStalePostingsCollapseToTheSameCanonicalUrl_MergesThemKeepingEarliestFirstSeenAndEveryPriceObservation()
    {
        // The real pre-existing ledger shape: one row written before the walk started storing
        // canonical URLs (a sid-bearing href) and one row written after (the bare canonical URL
        // the walk now stores directly). The survivor is the earlier, sid-bearing row, so the
        // migration has to update its Url to a value the row it's about to delete already holds
        // under the (VehicleVin, Source, Url) unique index, exercising that ordering against a
        // real SQLite database rather than a fixture where neither row already sits on the
        // canonical value.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";
        const string canonicalUrl = "https://www.cars.com/vehicledetail/abc123/";

        var run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl), run1, CancellationToken.None);

        var run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, canonicalUrl), run2, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        List<PostingEntity> postings = [.. db.Postings.Include(p => p.PriceObservations).Where(p => p.VehicleVin == "1HGCM82633A004352")];
        PostingEntity survivor = Assert.Single(postings);
        Assert.Equal(canonicalUrl, survivor.Url);
        Assert.Equal(run1.StartedAt, survivor.FirstSeen);
        Assert.Equal(run2.StartedAt, survivor.LastSeen);
        Assert.Equal(2, survivor.PriceObservations.Count);
        Assert.Contains(survivor.PriceObservations, o => o.Price == 18000m);
        Assert.Contains(survivor.PriceObservations, o => o.Price == 17000m);

        Assert.Single(db.LedgerMigrations, m => m.Name == "CanonicalizeWalkedPostingUrls");
    }

    [Fact]
    public async Task ApplyAll_CarvanaStalePostingCollapsesToItsCanonicalUrl_MergesItTooNotOnlyCarsCom()
    {
        // Carvana's own detail links carry the same kind of per-search-session id cars.com's do
        // (see WalkSites.CanonicalDetailUrl), so a ledger walked on carvana before the URL fix
        // needs the same merge cars.com gets, not just cars.com's own rows.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        const string sidBearingUrl = "https://www.carvana.com/vehicle/4754913?refSource=srp";
        const string canonicalUrl = "https://www.carvana.com/vehicle/4754913";

        var run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "carvana:Prius");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl, "carvana"), run1, CancellationToken.None);

        var run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), sources: "carvana:Prius");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 17000m, canonicalUrl, "carvana"), run2, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        PostingEntity survivor = Assert.Single(db.Postings.Where(p => p.VehicleVin == "1HGCM82633A004352"));
        Assert.Equal(canonicalUrl, survivor.Url);
        Assert.Equal(run1.StartedAt, survivor.FirstSeen);
        Assert.Equal(run2.StartedAt, survivor.LastSeen);
    }

    [Fact]
    public async Task ApplyAll_SingleAlreadyCanonicalPosting_RewritesUrlAndIsANoOpOnAnyLaterCall()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";
        const string canonicalUrl = "https://www.cars.com/vehicledetail/abc123/";

        var run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl), run1, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        PostingEntity posting = Assert.Single(db.Postings.Where(p => p.VehicleVin == "1HGCM82633A004352"));
        Assert.Equal(canonicalUrl, posting.Url);

        LedgerDataMigrations.ApplyAll(db);

        posting = Assert.Single(db.Postings.Where(p => p.VehicleVin == "1HGCM82633A004352"));
        Assert.Equal(canonicalUrl, posting.Url);
    }

    [Fact]
    public async Task ApplyAll_AlreadyRecordedAsApplied_SkipsRescanningAndLeavesADirtyRowUntouched()
    {
        // Proves "recorded so it never runs twice": once the migration's name is in
        // LedgerMigrations, ApplyAll must not rescan the ledger at all, even if a row that would
        // otherwise need canonicalizing shows up afterward.
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        const string sidBearingUrl = "https://www.cars.com/vehicledetail/abc123/?sid=xyz789";

        db.LedgerMigrations.Add(new LedgerMigrationEntity { Name = "CanonicalizeWalkedPostingUrls", AppliedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(CancellationToken.None);

        var run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, sidBearingUrl), run1, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        PostingEntity posting = Assert.Single(db.Postings.Where(p => p.VehicleVin == "1HGCM82633A004352"));
        Assert.Equal(sidBearingUrl, posting.Url);
        Assert.Single(db.LedgerMigrations, m => m.Name == "CanonicalizeWalkedPostingUrls");
    }
}
