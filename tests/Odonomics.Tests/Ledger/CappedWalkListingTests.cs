using Microsoft.EntityFrameworkCore;
using Odonomics.Cli;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Ledger;

/// <summary>Walks a real ledger fully and then again with a cap that misses one car, and proves the
/// rank pipeline (pricing, scoring, rendering) still lists the car the capped walk never reached.</summary>
public class CappedWalkListingTests
{
    private const string ReachedVin = "JTDKN3DU0A0000001";
    private const string MissedVin = "JTDKN3DU0A0000002";
    private const string Token = "cars.com:Prius";
    private static readonly DateTimeOffset FullWalkAt = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CappedWalkAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterFullWalkAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private static Scenario DaughterScenario { get; } = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static ListingCandidate Candidate(string vin, decimal price) => new()
    {
        Vin = vin,
        Source = "cars.com",
        Url = $"https://www.cars.com/vehicledetail/{vin}/",
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Trim = "LE",
        Price = price,
        Mileage = 40000,
    };

    private static async Task<RunEntity> StartWalkAsync(OdonomicsDbContext db, DateTimeOffset startedAt, bool capped)
    {
        var run = new RunEntity
        {
            Command = "walk",
            StartedAt = startedAt,
            Sources = capped ? $"{Token},{RunSources.PartialKey(Token)}" : Token,
        };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    /// <summary>A full walk that sees both cars, then a capped walk that reaches only the first one.</summary>
    private static async Task<OdonomicsDbContext> LedgerAfterFullThenCappedWalkAsync(LedgerTestDatabase testDb)
    {
        OdonomicsDbContext db = testDb.CreateContext();
        var upsert = new LedgerUpsertService(db);

        RunEntity fullWalk = await StartWalkAsync(db, FullWalkAt, capped: false);
        await upsert.UpsertAsync(Candidate(ReachedVin, 15000m), fullWalk, CancellationToken.None);
        await upsert.UpsertAsync(Candidate(MissedVin, 14000m), fullWalk, CancellationToken.None);

        RunEntity cappedWalk = await StartWalkAsync(db, CappedWalkAt, capped: true);
        await upsert.UpsertAsync(Candidate(ReachedVin, 15000m), cappedWalk, CancellationToken.None);
        return db;
    }

    private static async Task<List<VehicleEntity>> LoadVehiclesAsync(OdonomicsDbContext db) =>
        await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .OrderBy(v => v.Vin)
            .ToListAsync(CancellationToken.None);

    private static Score ScoreOf(VehicleEntity vehicle, Dictionary<string, DateTimeOffset> coverage)
    {
        PurchasePrice? purchasePrice = VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery);
        var forScoring = new VehicleForScoring
        {
            Vin = vehicle.Vin,
            Year = vehicle.Year,
            Make = vehicle.Make,
            Model = vehicle.Model,
            Mileage = vehicle.Mileage,
            LowestCurrentPrice = purchasePrice?.Asking,
            ShippingFee = purchasePrice?.ShippingFee,
        };
        return Scorer.Score(forScoring, DaughterScenario);
    }

    private static string RenderRank(IReadOnlyList<Score> scores)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        RankRenderer.Render(console, scores, null, new Dictionary<string, ResearchStatus>(), [], detail: false);
        return console.Output;
    }

    [Fact]
    public async Task RankPipeline_CappedWalkThatMissedACar_StillPricesScoresAndListsIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = await LedgerAfterFullThenCappedWalkAsync(testDb);
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(await db.Runs.ToListAsync(CancellationToken.None));
        List<VehicleEntity> vehicles = await LoadVehiclesAsync(db);
        VehicleEntity missed = vehicles.Single(v => v.Vin == MissedVin);

        Assert.Equal(FullWalkAt, missed.Postings.Single().LastSeen);
        Assert.Equal(new PurchasePrice(14000m, null), VehiclePricing.LowestCurrentPurchasePrice(missed, coverage, Fulfillment.Delivery));

        List<Score> scores = [.. vehicles.Select(v => ScoreOf(v, coverage))];

        Assert.All(scores, score => Assert.DoesNotContain(score.FailureReasons, reason => reason.Contains("no current asking price")));
        Assert.All(scores, score => Assert.True(score.Passes));
        string output = RenderRank(scores);
        Assert.Contains(ReachedVin, output);
        Assert.Contains(MissedVin, output);
    }

    [Fact]
    public async Task RankPipeline_FullWalkAfterTheCappedOneThatDoesNotSeeACar_ExcludesIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = await LedgerAfterFullThenCappedWalkAsync(testDb);
        RunEntity laterFullWalk = await StartWalkAsync(db, LaterFullWalkAt, capped: false);
        await new LedgerUpsertService(db).UpsertAsync(Candidate(ReachedVin, 15000m), laterFullWalk, CancellationToken.None);
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(await db.Runs.ToListAsync(CancellationToken.None));
        List<VehicleEntity> vehicles = await LoadVehiclesAsync(db);

        Score reached = ScoreOf(vehicles.Single(v => v.Vin == ReachedVin), coverage);
        Score missed = ScoreOf(vehicles.Single(v => v.Vin == MissedVin), coverage);

        Assert.True(reached.Passes);
        Assert.False(missed.Passes);
        Assert.Contains(missed.FailureReasons, reason => reason.Contains("no current asking price"));
    }
}
