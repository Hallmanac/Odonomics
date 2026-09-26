using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

/// <summary>Proves a walked page that says the car has sold marks the posting the ledger already holds
/// at that link, for that run only, and changes nothing else about it: not its last-seen stamp, which
/// would make the diff read the car as still listed, and not its price history, since a sold page carries
/// other cars' prices.</summary>
public class LedgerSoldTests
{
    private const string Url = "https://www.autotrader.com/cars-for-sale/vehicle/771234567";
    private static readonly DateTimeOffset FirstRunAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private static ListingCandidate Candidate(string url = Url, string source = "autotrader") => new()
    {
        Vin = "1HGCM82633A004352",
        Source = source,
        Url = url,
        Year = 2020,
        Make = "Toyota",
        Model = "Corolla Hybrid",
        Trim = "LE",
        Price = 18000m,
        Mileage = 40000,
    };

    private static async Task<RunEntity> StartRunAsync(OdonomicsDbContext db, DateTimeOffset startedAt)
    {
        var run = new RunEntity { Command = "walk", Sources = "autotrader:Corolla Hybrid", StartedAt = startedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    [Fact]
    public async Task MarkSoldAsync_LinkTheLedgerHolds_StampsTheRunAndTouchesNothingElse()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), first, CancellationToken.None);
        RunEntity second = await StartRunAsync(db, SecondRunAt);

        int marked = await service.MarkSoldAsync("autotrader", Url, second, CancellationToken.None);

        Assert.Equal(1, marked);
        PostingEntity posting = await db.Postings.Include(p => p.PriceObservations).SingleAsync();
        Assert.Equal(SecondRunAt, posting.SoldSeenAt);
        Assert.Equal(FirstRunAt, posting.LastSeen);
        PriceObservationEntity observation = Assert.Single(posting.PriceObservations);
        Assert.Equal(18000m, observation.Price);
    }

    [Fact]
    public async Task MarkSoldAsync_LinkTheLedgerDoesNotHold_MarksNothing()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), first, CancellationToken.None);
        RunEntity second = await StartRunAsync(db, SecondRunAt);

        int otherLink = await service.MarkSoldAsync("autotrader", "https://www.autotrader.com/cars-for-sale/vehicle/1", second, CancellationToken.None);
        int otherSource = await service.MarkSoldAsync("cars.com", Url, second, CancellationToken.None);

        Assert.Equal(0, otherLink);
        Assert.Equal(0, otherSource);
        Assert.Null((await db.Postings.SingleAsync()).SoldSeenAt);
    }

    [Fact]
    public async Task UpsertAsync_NewPosting_StartsWithNoSoldStamp()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);

        await service.UpsertAsync(Candidate(), first, CancellationToken.None);

        Assert.Null((await db.Postings.SingleAsync()).SoldSeenAt);
    }
}
