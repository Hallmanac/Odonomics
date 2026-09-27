using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

/// <summary>Proves a known posting whose search card the render wait gave up on is stamped for that
/// run only, and changes nothing else about it: not its last-seen stamp, which would make the diff
/// read the car as still measured, and not its price history, since the render wait never actually
/// read the card.</summary>
public class LedgerUnrenderedCardTests
{
    private const string Url = "https://www.cars.com/vehicledetail/known-unrendered/";
    private static readonly DateTimeOffset FirstRunAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private static ListingCandidate Candidate(string url = Url, string source = "cars.com") => new()
    {
        Vin = "1HGCM82633A004352",
        Source = source,
        Url = url,
        Year = 2020,
        Make = "Honda",
        Model = "Insight",
        Trim = "EX",
        Price = 18000m,
        Mileage = 40000,
    };

    private static async Task<RunEntity> StartRunAsync(OdonomicsDbContext db, DateTimeOffset startedAt)
    {
        var run = new RunEntity { Command = "walk", Sources = "cars.com:Insight", StartedAt = startedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    [Fact]
    public async Task MarkCardsUnrenderedAsync_LinkTheLedgerHolds_StampsTheRunAndTouchesNothingElse()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), first, CancellationToken.None);
        RunEntity second = await StartRunAsync(db, SecondRunAt);

        await service.MarkCardsUnrenderedAsync("cars.com", [Url], second, CancellationToken.None);

        PostingEntity posting = await db.Postings.Include(p => p.PriceObservations).SingleAsync();
        Assert.Equal(SecondRunAt, posting.CardUnrenderedSeenAt);
        Assert.Equal(FirstRunAt, posting.LastSeen);
        PriceObservationEntity observation = Assert.Single(posting.PriceObservations);
        Assert.Equal(18000m, observation.Price);
    }

    [Fact]
    public async Task MarkCardsUnrenderedAsync_LinkTheLedgerDoesNotHold_MarksNothing()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), first, CancellationToken.None);
        RunEntity second = await StartRunAsync(db, SecondRunAt);

        await service.MarkCardsUnrenderedAsync("cars.com", ["https://www.cars.com/vehicledetail/some-other-car/"], second, CancellationToken.None);
        await service.MarkCardsUnrenderedAsync("carvana", [Url], second, CancellationToken.None);

        Assert.Null((await db.Postings.SingleAsync()).CardUnrenderedSeenAt);
    }

    [Fact]
    public async Task UpsertAsync_NewPosting_StartsWithNoUnrenderedStamp()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);

        await service.UpsertAsync(Candidate(), first, CancellationToken.None);

        Assert.Null((await db.Postings.SingleAsync()).CardUnrenderedSeenAt);
    }
}
