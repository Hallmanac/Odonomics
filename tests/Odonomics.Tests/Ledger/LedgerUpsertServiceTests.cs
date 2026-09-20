using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class LedgerUpsertServiceTests
{
    private static ListingCandidate Candidate(string vin, decimal price, string url = "https://example.com/1", string source = "auto.dev") => new()
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

    private static RunEntity Run(DateTimeOffset startedAt, string command = "search") => new()
    {
        Command = command,
        Sources = "auto.dev",
        StartedAt = startedAt,
    };

    [Fact]
    public async Task UpsertAsync_FirstSighting_CreatesVehiclePostingAndPriceObservation()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        UpsertOutcome outcome = await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m), run, CancellationToken.None);

        Assert.True(outcome.VehicleIsNew);
        Assert.True(outcome.PostingIsNew);
        Assert.Null(outcome.PreviousPrice);

        VehicleEntity vehicle = await db.Vehicles.FindAsync(["1HGCM82633A004352"]) ?? throw new InvalidOperationException();
        Assert.Equal("Toyota", vehicle.Make);
        Assert.Single(db.Postings);
        Assert.Single(db.PriceObservations);
        Assert.Equal(18000m, db.PriceObservations.Single().Price);
    }

    [Fact]
    public async Task UpsertAsync_SamePriceSeenAgain_UpdatesLastSeenButAppendsNoObservation()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        UpsertOutcome outcome = await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m), run2, CancellationToken.None);

        Assert.False(outcome.VehicleIsNew);
        Assert.False(outcome.PostingIsNew);
        Assert.False(outcome.PriceChanged);
        Assert.Single(db.PriceObservations); // no new observation for an unchanged price
        Assert.Equal(run2.StartedAt, db.Postings.Single().LastSeen);
    }

    [Fact]
    public async Task UpsertAsync_PriceChanged_AppendsNewObservationAndReportsPreviousPrice()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        UpsertOutcome outcome = await service.UpsertAsync(Candidate("1HGCM82633A004352", 17500m), run2, CancellationToken.None);

        Assert.True(outcome.PriceChanged);
        Assert.Equal(18000m, outcome.PreviousPrice);
        Assert.Equal(2, db.PriceObservations.Count());
    }

    [Fact]
    public async Task UpsertAsync_SecondPostingForSameVehicle_DoesNotCreateSecondVehicle()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com"), run, CancellationToken.None);
        UpsertOutcome outcome = await service.UpsertAsync(Candidate("1HGCM82633A004352", 18500m, "https://carvana.com/b", "carvana"), run, CancellationToken.None);

        Assert.False(outcome.VehicleIsNew);
        Assert.True(outcome.PostingIsNew);
        Assert.Single(db.Vehicles);
        Assert.Equal(2, db.Postings.Count());
    }
}
