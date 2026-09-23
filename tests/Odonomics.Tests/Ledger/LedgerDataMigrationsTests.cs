using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Odonomics.Marketcheck;

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

    [Fact]
    public async Task ApplyAll_CarvanaPostingsWithNoDealer_LinksThemToOneCarvanaDealerAndLeavesNamedHubsAndCarsComAlone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        var run = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsert.UpsertAsync(Candidate("JTDEAMDE3NJ058833", 18000m, "https://www.carvana.com/vehicle/1", "carvana"), run, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("JTDEBRBE8LJ019584", 19000m, "https://www.carvana.com/vehicle/2", "carvana"), run, CancellationToken.None);
        ListingCandidate hub = Candidate("4T1B21HK8KU518914", 20000m, "https://www.carvana.com/vehicle/3", "carvana");
        await upsert.UpsertAsync(new ListingCandidate
        {
            Vin = hub.Vin, Source = hub.Source, Url = hub.Url, Year = hub.Year, Make = hub.Make, Model = hub.Model,
            Trim = hub.Trim, Price = hub.Price, Mileage = hub.Mileage, DealerName = "Carvana Winder",
        }, run, CancellationToken.None);
        await upsert.UpsertAsync(Candidate("1HGCM82633A004352", 21000m, "https://www.cars.com/vehicledetail/abc123/"), run, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        List<PostingEntity> postings = [.. db.Postings.Include(p => p.Dealer).OrderBy(p => p.Url)];
        // Ordered by URL: the cars.com posting first, then the three carvana ones.
        Assert.Equal([null, "Carvana", "Carvana", "Carvana Winder"], postings.Select(p => p.Dealer?.Name));
        Assert.Same(postings[1].Dealer, postings[2].Dealer);
        Assert.Null(postings[1].Dealer!.Location);
        Assert.Single(db.Dealers, d => d.NormalizedName == "CARVANA");
        Assert.Single(db.LedgerMigrations, m => m.Name == "StampCarvanaFallbackDealer");
    }

    [Fact]
    public async Task ApplyAll_CarvanaDealerAlreadyExists_ReusesItAndIsANoOpOnAnyLaterCall()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        var run = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        db.Dealers.Add(new DealerEntity { Name = "Carvana", NormalizedName = "CARVANA", NormalizedLocation = "" });
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(Candidate("JTDEAMDE3NJ058833", 18000m, "https://www.carvana.com/vehicle/1", "carvana"), run, CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        Assert.Single(db.Dealers);
        PostingEntity posting = Assert.Single(db.Postings.Include(p => p.Dealer));
        Assert.Equal("Carvana", posting.Dealer!.Name);

        // A posting that shows up undealered after the migration ran (it never will from the walk,
        // which now always stamps one) is not touched by a later startup.
        await upsert.UpsertAsync(Candidate("JTDEBRBE8LJ019584", 19000m, "https://www.carvana.com/vehicle/2", "carvana"), run, CancellationToken.None);
        LedgerDataMigrations.ApplyAll(db);

        Assert.Null(db.Postings.Include(p => p.Dealer).Single(p => p.VehicleVin == "JTDEBRBE8LJ019584").Dealer);
    }

    private static readonly DateTimeOffset WalkedAt = new(2026, 9, 23, 2, 55, 0, TimeSpan.Zero);

    private static ListingCandidate CarvanaCandidate(string vin, string? dealerName = null, string? dealerLocation = null) => new()
    {
        Vin = vin,
        Source = "carvana",
        Url = $"https://www.carvana.com/vehicle/{vin}",
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Price = 20000m,
        Mileage = 40000,
        DealerName = dealerName,
        DealerLocation = dealerLocation,
    };

    // The upsert drops the location for Carvana names, so the located rows an older walk left behind
    // have to be seeded directly to exercise the fold.
    private static DealerEntity LocatedDealer(string name, string location) => new()
    {
        Name = name,
        Location = location,
        NormalizedName = name.ToUpperInvariant(),
        NormalizedLocation = location.Replace(",", "").ToUpperInvariant(),
    };

    private static void AddHistory(OdonomicsDbContext db, string vin, params VinHistoryListing[] listings)
    {
        db.VinRecords.Add(new VinRecordEntity
        {
            Vin = vin,
            DecodedAt = WalkedAt,
            DecodeRawJson = "",
            HistoryRawJson = JsonSerializer.Serialize(listings.ToList()),
        });
        db.SaveChanges();
    }

    private static VinHistoryListing Stay(string dealer, DateTimeOffset firstSeen, DateTimeOffset lastSeen) =>
        new(dealer, null, null, firstSeen, lastSeen, 20000m, 40000, null);

    [Fact]
    public async Task ApplyAll_CarvanaPostingsOnTheBareOrALocatedCarvanaRow_MoveToTheHubTheirVinHistoryNamesAndStayBareOnlyWhenNoHubIsKnown()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var run = Run(WalkedAt, sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        // Undealered (so the earlier stamp migration puts it on the bare row), history names a hub.
        await upsert.UpsertAsync(CarvanaCandidate("JTDEAMDE3NJ058833"), run, CancellationToken.None);
        // Undealered, and the only hub its history names is a stay from months before the walk.
        await upsert.UpsertAsync(CarvanaCandidate("JTDEBRBE8LJ019584"), run, CancellationToken.None);
        // The older walk stored the pickup city as the dealer's location; history names a hub.
        await upsert.UpsertAsync(CarvanaCandidate("JTDBCMFE9PJ003977"), run, CancellationToken.None);
        // The same located row, and no history at all.
        await upsert.UpsertAsync(CarvanaCandidate("4T1B21HK8KU518914"), run, CancellationToken.None);
        DealerEntity located = LocatedDealer("Carvana", "Orlando, FL");
        db.Dealers.Add(located);
        foreach (PostingEntity posting in db.Postings.Where(p => p.VehicleVin == "JTDBCMFE9PJ003977" || p.VehicleVin == "4T1B21HK8KU518914"))
        {
            posting.Dealer = located;
        }

        await db.SaveChangesAsync(CancellationToken.None);
        AddHistory(db, "JTDEAMDE3NJ058833", Stay("Carvana Winder", new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 25, 0, TimeSpan.Zero)));
        AddHistory(db, "JTDEBRBE8LJ019584", Stay("Carvana Fairburn", new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 26, 0, 0, 0, TimeSpan.Zero)));
        AddHistory(db, "JTDBCMFE9PJ003977", Stay("Carvana Belton", new DateTimeOffset(2026, 9, 22, 2, 28, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 34, 0, TimeSpan.Zero)));

        LedgerDataMigrations.ApplyAll(db);

        Dictionary<string, string?> dealerByVin = db.Postings.Include(p => p.Dealer).ToDictionary(p => p.VehicleVin, p => p.Dealer?.Name);
        Assert.Equal("Carvana Winder", dealerByVin["JTDEAMDE3NJ058833"]);
        Assert.Equal("Carvana", dealerByVin["JTDEBRBE8LJ019584"]);
        Assert.Equal("Carvana Belton", dealerByVin["JTDBCMFE9PJ003977"]);
        Assert.Equal("Carvana", dealerByVin["4T1B21HK8KU518914"]);

        DealerEntity bare = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana"));
        Assert.NotEqual(located.Id, bare.Id);
        Assert.DoesNotContain(db.Dealers, d => d.Id == located.Id);
        Assert.Equal("", bare.NormalizedLocation);
        Assert.Null(bare.Location);
        Assert.Equal(2, db.Postings.Count(p => p.DealerId == bare.Id));
        Assert.All(db.Dealers.Where(d => d.NormalizedName.StartsWith("CARVANA ")), hub =>
        {
            Assert.Equal("", hub.NormalizedLocation);
            Assert.Null(hub.Location);
        });
        Assert.Equal(["Carvana", "Carvana Belton", "Carvana Winder"], db.Dealers.Select(d => d.Name).OrderBy(n => n).ToList());
        Assert.Single(db.LedgerMigrations, m => m.Name == "ResolveCarvanaHubDealers");
    }

    [Fact]
    public async Task ApplyAll_LocatedCarvanaRowsExist_FoldsThemIntoTheBareRowAndClearsItsGradeSoTheNextGradeRunRedoesIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var run = Run(WalkedAt, sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await upsert.UpsertAsync(CarvanaCandidate("JTDEAMDE3NJ058833"), run, CancellationToken.None);
        await upsert.UpsertAsync(CarvanaCandidate("JTDEBRBE8LJ019584"), run, CancellationToken.None);
        await upsert.UpsertAsync(CarvanaCandidate("4T1B21HK8KU518914"), run, CancellationToken.None);
        DealerEntity orlando = LocatedDealer("Carvana", "Orlando, FL");
        DealerEntity atlanta = LocatedDealer("Carvana", "Atlanta, GA");
        DealerEntity existingBare = new() { Name = "Carvana", NormalizedName = "CARVANA", NormalizedLocation = "" };
        db.Dealers.AddRange(orlando, atlanta, existingBare);
        Dictionary<string, PostingEntity> postingByVin = db.Postings.ToDictionary(p => p.VehicleVin);
        postingByVin["JTDEAMDE3NJ058833"].Dealer = orlando;
        postingByVin["JTDEBRBE8LJ019584"].Dealer = atlanta;
        await db.SaveChangesAsync(CancellationToken.None);
        // The dealer grade run Brian may have made before this migration: a located row graded from
        // whichever hub's card matched its city, and the bare row stamped as checked too.
        DateTimeOffset gradedAt = new(2026, 9, 23, 5, 0, 0, TimeSpan.Zero);
        foreach (DealerEntity dealer in db.Dealers.Where(d => d.Name == "Carvana"))
        {
            dealer.Grade = "F";
            dealer.GradeReason = "matched a hub's card";
            dealer.GradeCheckedAt = gradedAt;
        }

        DealerEntity winder = new() { Name = "Carvana Winder", NormalizedName = "CARVANA WINDER", NormalizedLocation = "", Grade = "B", GradeCheckedAt = gradedAt };
        db.Dealers.Add(winder);
        await db.SaveChangesAsync(CancellationToken.None);
        AddHistory(db, "4T1B21HK8KU518914", Stay("Carvana Winder", new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 25, 0, TimeSpan.Zero)));

        LedgerDataMigrations.ApplyAll(db);

        DealerEntity bare = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana"));
        Assert.Equal(existingBare.Id, bare.Id);
        Assert.Equal("", bare.NormalizedLocation);
        Assert.Null(bare.Location);
        Assert.Null(bare.Grade);
        Assert.Null(bare.GradeReason);
        Assert.Null(bare.GradeCheckedAt);
        Assert.Equal(2, db.Postings.Count(p => p.DealerId == bare.Id));

        // The undealered posting was stamped bare by the earlier migration, then moved to the
        // hub that already had a row; that row keeps the grade it earned under its own name.
        DealerEntity existingHub = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana Winder"));
        Assert.Equal(winder.Id, existingHub.Id);
        Assert.Equal("B", existingHub.Grade);
        Assert.Equal(gradedAt, existingHub.GradeCheckedAt);
        Assert.Equal(1, db.Postings.Count(p => p.DealerId == existingHub.Id));
    }

    [Fact]
    public async Task ApplyAll_OnlyLocatedCarvanaRowsExistAndNoBareOne_CreatesTheBareRowForTheirPostings()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var run = Run(WalkedAt, sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(CarvanaCandidate("JTDEAMDE3NJ058833"), run, CancellationToken.None);
        DealerEntity located = LocatedDealer("Carvana", "Orlando, FL");
        db.Postings.Single().Dealer = located;
        await db.SaveChangesAsync(CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        DealerEntity bare = Assert.Single(db.Dealers);
        Assert.Equal("Carvana", bare.Name);
        Assert.Equal("", bare.NormalizedLocation);
        Assert.Null(bare.Location);
        Assert.Equal(bare.Id, Assert.Single(db.Postings).DealerId);
    }

    [Fact]
    public async Task ApplyAll_LocatedHubRowsExist_FoldsThemIntoOneLocationLessRowPerHubKeepingItsGrade()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var run = Run(WalkedAt, sources: "carvana:Prius");
        db.Runs.Add(run);
        DateTimeOffset gradedAt = new(2026, 9, 23, 5, 0, 0, TimeSpan.Zero);
        // The older walk stored the pickup city beside a named hub. Winder has a located row and a
        // location-less twin; Fairburn has only located rows (two of them); Belton has one located
        // row that the VIN history of an undealered posting also names.
        DealerEntity winderTwin = new() { Name = "Carvana Winder", NormalizedName = "CARVANA WINDER", NormalizedLocation = "", Grade = "B", GradeCheckedAt = gradedAt };
        DealerEntity winderLocated = new() { Name = "Carvana Winder", Location = "Orlando, FL", NormalizedName = "CARVANA WINDER", NormalizedLocation = "ORLANDO FL", Grade = "F", GradeCheckedAt = gradedAt };
        DealerEntity fairburnOrlando = new() { Name = "Carvana Fairburn", Location = "Orlando, FL", NormalizedName = "CARVANA FAIRBURN", NormalizedLocation = "ORLANDO FL", Grade = "A", GradeCheckedAt = gradedAt };
        DealerEntity fairburnAtlanta = new() { Name = "Carvana Fairburn", Location = "Atlanta, GA", NormalizedName = "CARVANA FAIRBURN", NormalizedLocation = "ATLANTA GA" };
        DealerEntity beltonLocated = new() { Name = "Carvana Belton", Location = "Orlando, FL", NormalizedName = "CARVANA BELTON", NormalizedLocation = "ORLANDO FL" };
        db.Dealers.AddRange(winderTwin, winderLocated, fairburnOrlando, fairburnAtlanta, beltonLocated);
        await db.SaveChangesAsync(CancellationToken.None);

        var upsert = new LedgerUpsertService(db);
        foreach (string vin in new[] { "JTDEAMDE3NJ058833", "JTDEBRBE8LJ019584", "JTDBCMFE9PJ003977", "4T1B21HK8KU518914", "5YFBURHE5HP600000" })
        {
            await upsert.UpsertAsync(CarvanaCandidate(vin), run, CancellationToken.None);
        }

        Dictionary<string, PostingEntity> postingByVin = db.Postings.ToDictionary(p => p.VehicleVin);
        postingByVin["JTDEAMDE3NJ058833"].Dealer = winderLocated;
        postingByVin["JTDEBRBE8LJ019584"].Dealer = fairburnOrlando;
        postingByVin["JTDBCMFE9PJ003977"].Dealer = fairburnAtlanta;
        postingByVin["4T1B21HK8KU518914"].Dealer = beltonLocated;
        // Undealered until the stamp migration puts it on the bare row; history then names Belton.
        AddHistory(db, "5YFBURHE5HP600000", Stay("Carvana Belton", new DateTimeOffset(2026, 9, 22, 2, 28, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 34, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync(CancellationToken.None);

        LedgerDataMigrations.ApplyAll(db);

        Assert.All(db.Dealers, d => Assert.Equal("", d.NormalizedLocation));
        Assert.Equal(["Carvana", "Carvana Belton", "Carvana Fairburn", "Carvana Winder"], db.Dealers.Select(d => d.Name).OrderBy(n => n).ToList());

        DealerEntity winder = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana Winder"));
        Assert.Equal(winderTwin.Id, winder.Id);
        Assert.Equal("B", winder.Grade);
        Assert.Equal(1, db.Postings.Count(p => p.DealerId == winder.Id));

        DealerEntity fairburn = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana Fairburn"));
        Assert.Equal(fairburnOrlando.Id, fairburn.Id);
        Assert.Null(fairburn.Location);
        Assert.Equal("A", fairburn.Grade);
        Assert.Equal(2, db.Postings.Count(p => p.DealerId == fairburn.Id));

        DealerEntity belton = Assert.Single(db.Dealers.Where(d => d.Name == "Carvana Belton"));
        Assert.Equal(beltonLocated.Id, belton.Id);
        Assert.Null(belton.Location);
        Assert.Equal(2, db.Postings.Count(p => p.DealerId == belton.Id));
    }

    [Fact]
    public async Task ApplyAll_ALaterStartupAfterTheHubMigrationRan_LeavesAPostingHistoryNowNamesAHubForUntouched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);
        var run = Run(WalkedAt, sources: "carvana:Prius");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        await upsert.UpsertAsync(CarvanaCandidate("JTDEAMDE3NJ058833"), run, CancellationToken.None);
        LedgerDataMigrations.ApplyAll(db);
        AddHistory(db, "JTDEAMDE3NJ058833", Stay("Carvana Winder", new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 25, 0, TimeSpan.Zero)));

        LedgerDataMigrations.ApplyAll(db);

        Assert.Equal("Carvana", db.Postings.Include(p => p.Dealer).Single().Dealer!.Name);
    }
}
