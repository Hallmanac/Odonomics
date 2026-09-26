using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

/// <summary>Proves a walk can keep a listing current from its search-page card alone: the touch stamps
/// the run's own StartedAt on the posting so the existing diff reads it as still listed, appends a price
/// observation only for a known price that differs, and leaves the dealer, the shipping fee, and the
/// vehicle row as the last detail visit left them.</summary>
public class LedgerTouchTests
{
    private const string Vin = "1HGCM82633A004352";
    private const string Url = "https://www.carvana.com/vehicle/4636332";
    private static readonly DateTimeOffset FirstRunAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private static Scenario DaughterScenario { get; } = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static ListingCandidate Candidate(decimal price = 18000m, string vin = Vin, string url = Url, string source = "carvana") => new()
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
        DealerName = "Carvana Winder",
        DealerNameIsFallback = false,
        ShippingFee = 690m,
    };

    private static async Task<RunEntity> StartRunAsync(OdonomicsDbContext db, DateTimeOffset startedAt, string sources = "carvana:Prius")
    {
        var run = new RunEntity { Command = "walk", Sources = sources, StartedAt = startedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    private static async Task<(LedgerUpsertService Service, RunEntity SecondRun)> SeededAsync(OdonomicsDbContext db)
    {
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), first, CancellationToken.None);
        return (service, await StartRunAsync(db, SecondRunAt));
    }

    private static Task<TouchOutcome> TouchOneAsync(LedgerUpsertService service, string source, string url, decimal? cardPrice, RunEntity run) =>
        service.TouchAsync(source, new Dictionary<string, decimal?> { [url] = cardPrice }, run, CancellationToken.None);

    [Fact]
    public async Task TouchAsync_SeenAgainAtALowerCardPrice_YieldsAPriceDropAndNoGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);

        TouchOutcome outcome = await TouchOneAsync(service, "carvana", Url, 17000m, second);
        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        Assert.Equal(1, outcome.Found);
        Assert.Equal(1, outcome.PriceChanged);
        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal((18000m, 17000m), (drop.PreviousPrice, drop.CurrentPrice));
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.New);
    }

    [Fact]
    public async Task TouchAsync_APostingNoCardWasTouchedFor_YieldsGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (_, RunEntity second) = await SeededAsync(db);

        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        GonePostingEntry gone = Assert.Single(diff.Gone);
        Assert.Equal((Vin, Url), (gone.Vin, gone.Url));
    }

    [Fact]
    public async Task TouchAsync_StampsTheRunsOwnStartedAtAndLeavesDealerFeeAndVehicleAsTheyWere()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);

        await TouchOneAsync(service, "carvana", Url, 17000m, second);

        PostingEntity posting = await db.Postings.Include(p => p.Dealer).SingleAsync();
        Assert.Equal(SecondRunAt, posting.LastSeen);
        Assert.Equal(FirstRunAt, posting.FirstSeen);
        Assert.Equal("Carvana Winder", posting.Dealer!.Name);
        Assert.Equal(690m, posting.ShippingFee);
        VehicleEntity vehicle = await db.Vehicles.SingleAsync();
        Assert.Equal(FirstRunAt, vehicle.LastSeen);
        Assert.Equal(40000, vehicle.Mileage);
    }

    [Fact]
    public async Task TouchAsync_TheSamePriceAsTheLatestObservation_AppendsNothing()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);

        TouchOutcome outcome = await TouchOneAsync(service, "carvana", Url, 18000m, second);

        Assert.Equal(1, outcome.Found);
        Assert.Equal(0, outcome.PriceChanged);
        Assert.Single(db.PriceObservations);
        Assert.Equal(SecondRunAt, (await db.Postings.SingleAsync()).LastSeen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(349)]
    public async Task TouchAsync_ACardPriceThatCouldNotBeReadIsStillATouchWithoutAPriceObservation(int? unreadablePrice)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);

        TouchOutcome outcome = await TouchOneAsync(service, "carvana", Url, unreadablePrice, second);
        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        Assert.Equal(1, outcome.Found);
        Assert.Equal(0, outcome.PriceChanged);
        Assert.Single(db.PriceObservations);
        Assert.Equal(SecondRunAt, (await db.Postings.SingleAsync()).LastSeen);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task TouchAsync_ALinkTheLedgerDoesNotHold_WritesNothing()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);

        TouchOutcome wrongUrl = await TouchOneAsync(service, "carvana", "https://www.carvana.com/vehicle/999", 17000m, second);
        TouchOutcome wrongSource = await TouchOneAsync(service, "cars.com", Url, 17000m, second);

        Assert.Equal(0, wrongUrl.Found);
        Assert.Equal(0, wrongSource.Found);
        Assert.Single(db.PriceObservations);
        Assert.Equal(FirstRunAt, (await db.Postings.SingleAsync()).LastSeen);
    }

    [Fact]
    public async Task TouchAsync_APostingAlreadyObservedThisRunByADetailVisit_GetsNoSecondObservationAtTheSameInstant()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);
        await service.UpsertAsync(Candidate(price: 17500m), second, CancellationToken.None);

        TouchOutcome outcome = await TouchOneAsync(service, "carvana", Url, 17000m, second);

        Assert.Equal(1, outcome.Found);
        Assert.Equal(0, outcome.PriceChanged);
        Assert.Equal([18000m, 17500m], (await db.PriceObservations.ToListAsync()).OrderBy(o => o.ObservedAt).Select(o => o.Price));
    }

    [Fact]
    public async Task TouchAsync_ManyLinks_TouchesEachOneInASingleSave()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);
        string otherUrl = "https://www.carvana.com/vehicle/5";
        await service.UpsertAsync(Candidate(vin: "JTDKN3DU5A0000002", url: otherUrl), (await db.Runs.FirstAsync()), CancellationToken.None);

        TouchOutcome outcome = await service.TouchAsync(
            "carvana",
            new Dictionary<string, decimal?> { [Url] = 17000m, [otherUrl] = 18000m, ["https://www.carvana.com/vehicle/999"] = 1m },
            second,
            CancellationToken.None);

        Assert.Equal(new TouchOutcome(2, 1), outcome);
        Assert.All(await db.Postings.ToListAsync(), p => Assert.Equal(SecondRunAt, p.LastSeen));
    }

    [Fact]
    public async Task KnownUrlsAsync_ListsOnlyThePostingsOfThatSource()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);
        await service.UpsertAsync(Candidate(vin: "JTDKN3DU5A0000001", url: "https://www.cars.com/vehicledetail/aaa/", source: "cars.com"), second, CancellationToken.None);

        HashSet<string> carvana = await service.KnownUrlsAsync("carvana", CancellationToken.None);
        HashSet<string> autotrader = await service.KnownUrlsAsync("autotrader", CancellationToken.None);

        Assert.Equal([Url], carvana);
        Assert.Empty(autotrader);
    }

    [Fact]
    public async Task KnownPostingCountsAsync_CountsPostingsBySourceAndModel()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await SeededAsync(db);
        await service.UpsertAsync(Candidate(vin: "JTDKN3DU5A0000001", url: "https://www.carvana.com/vehicle/2"), second, CancellationToken.None);
        await service.UpsertAsync(Candidate(vin: "JTDKN3DU5A0000002", url: "https://www.cars.com/vehicledetail/aaa/", source: "cars.com"), second, CancellationToken.None);

        Dictionary<(string Source, string Model), int> counts = await service.KnownPostingCountsAsync(CancellationToken.None);

        Assert.Equal(2, counts[("carvana", "Prius")]);
        Assert.Equal(1, counts[("cars.com", "Prius")]);
        Assert.False(counts.ContainsKey(("autotrader", "Prius")));
    }
}
