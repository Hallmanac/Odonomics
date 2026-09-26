using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Odonomics.Marketcheck;

namespace Odonomics.Tests.Ledger;

public class LedgerUpsertServiceTests
{
    private static ListingCandidate Candidate(
        string vin,
        decimal price,
        string url = "https://example.com/1",
        string source = "auto.dev",
        string? dealerName = null,
        string? dealerLocation = null,
        decimal? shippingFee = null,
        decimal? pickupFee = null,
        string? pickupLocation = null) => new()
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
        DealerName = dealerName,
        DealerLocation = dealerLocation,
        ShippingFee = shippingFee,
        PickupFee = pickupFee,
        PickupLocation = pickupLocation,
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

    [Fact]
    public async Task UpsertAsync_CandidateNamesADealer_CreatesAndLinksIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, dealerName: "Holler Honda", dealerLocation: "Winter Park, FL"), run, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal("Holler Honda", dealer.Name);
        Assert.Equal("Winter Park, FL", dealer.Location);
        PostingEntity posting = Assert.Single(db.Postings);
        Assert.Equal(dealer.Id, posting.DealerId);
    }

    [Fact]
    public async Task UpsertAsync_SameDealerDifferentCasingAndSpacing_ResolvesToOneDealerRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com", "Holler Honda", "Winter Park, FL"), run, CancellationToken.None);
        await service.UpsertAsync(Candidate("5YJ3E1EA1KF000000", 22000m, "https://cars.com/b", "cars.com", "  holler   honda  ", "winter park, fl"), run, CancellationToken.None);

        Assert.Single(db.Dealers);
    }

    [Theory]
    [InlineData("Saint Augustine, FL", "St. Augustine, FL")]
    [InlineData("St. Augustine, FL", "Saint Augustine, FL")]
    public async Task UpsertAsync_SameDealerCityAbbreviatedInOneSourceAndSpelledOutInAnother_ResolvesToOneDealerRow(string firstLocation, string secondLocation)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://cars.com/a", "cars.com", "Jack Hanania Chevrolet", firstLocation), run, CancellationToken.None);
        await service.UpsertAsync(Candidate("5YJ3E1EA1KF000000", 22000m, "https://cars.com/b", "cars.com", "Jack Hanania Chevrolet", secondLocation), run, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal(firstLocation, dealer.Location);
        Assert.Equal("SAINT AUGUSTINE FL", dealer.NormalizedLocation);
        Assert.All(db.Postings, p => Assert.Equal(dealer.Id, p.DealerId));
    }

    [Fact]
    public async Task UpsertAsync_LaterSightingCarriesNoDealer_KeepsThePostingsExistingDealerLink()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, dealerName: "Holler Honda"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m), run2, CancellationToken.None);

        PostingEntity posting = Assert.Single(db.Postings);
        Assert.NotNull(posting.DealerId);
    }

    [Fact]
    public async Task UpsertAsync_FallbackDealerSightingOfAPostingLinkedToANamedHub_KeepsTheHubLinkAndMintsNoFallbackRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana", dealerName: "Carvana Winder"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        ListingCandidate fallbackSighting = Candidate("1HGCM82633A004352", 18000m, source: "carvana", dealerName: "Carvana") with { DealerNameIsFallback = true };
        await service.UpsertAsync(fallbackSighting, run2, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal("Carvana Winder", dealer.Name);
        Assert.Equal(dealer.Id, Assert.Single(db.Postings).DealerId);
    }

    [Fact]
    public async Task UpsertAsync_FallbackDealerSightingOfAPostingWithNoDealer_LinksTheFallbackDealer()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        ListingCandidate fallbackSighting = Candidate("1HGCM82633A004352", 18000m, source: "carvana", dealerName: "Carvana") with { DealerNameIsFallback = true };
        await service.UpsertAsync(fallbackSighting, run, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal("Carvana", dealer.Name);
        Assert.Null(dealer.Location);
        Assert.Equal(dealer.Id, Assert.Single(db.Postings).DealerId);
    }

    private static readonly DateTimeOffset FirstWalk = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterWalk = new(2026, 9, 23, 3, 0, 0, TimeSpan.Zero);

    private static ListingCandidate CarvanaFallbackSighting(string vin) =>
        Candidate(vin, 18000m, source: "carvana", dealerName: "Carvana") with { DealerNameIsFallback = true };

    private static async Task AddHistoryAsync(OdonomicsDbContext db, string vin, string hub, DateTimeOffset firstSeen, DateTimeOffset lastSeen)
    {
        List<VinHistoryListing> listings = [new VinHistoryListing(hub, null, null, firstSeen, lastSeen, 18000m, 40000, null)];
        db.VinRecords.Add(new VinRecordEntity { Vin = vin, DecodedAt = lastSeen, DecodeRawJson = "", HistoryRawJson = JsonSerializer.Serialize(listings) });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task UpsertAsync_LaterWalkOfAPostingOnTheBareCarvanaRowAndHistoryNamesAHub_MovesItToTheHubRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        const string vin = "1HGCM82633A004352";

        RunEntity run1 = Run(FirstWalk);
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run1, CancellationToken.None);
        Assert.Equal("Carvana", db.Postings.Include(p => p.Dealer).Single().Dealer!.Name);

        await AddHistoryAsync(db, vin, "Carvana Winder", FirstWalk.AddDays(-4), FirstWalk.AddHours(13));
        RunEntity run2 = Run(LaterWalk);
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run2, CancellationToken.None);

        PostingEntity posting = db.Postings.Include(p => p.Dealer).Single();
        Assert.Equal("Carvana Winder", posting.Dealer!.Name);
        Assert.Null(posting.Dealer.Location);
        Assert.Equal("", posting.Dealer.NormalizedLocation);
    }

    [Fact]
    public async Task UpsertAsync_LaterWalkOfAPostingOnALocatedCarvanaRowAndHistoryNamesAHub_MovesItToTheHubRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        const string vin = "1HGCM82633A004352";

        RunEntity run1 = Run(FirstWalk);
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate(vin, 18000m, source: "carvana", dealerName: "Carvana", dealerLocation: "Orlando, FL"), run1, CancellationToken.None);
        await AddHistoryAsync(db, vin, "Carvana Winder", FirstWalk.AddDays(-4), FirstWalk.AddHours(13));

        RunEntity run2 = Run(LaterWalk);
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run2, CancellationToken.None);

        Assert.Equal("Carvana Winder", db.Postings.Include(p => p.Dealer).Single().Dealer!.Name);
    }

    [Fact]
    public async Task UpsertAsync_FirstWalkOfACarvanaPostingWhoseHistoryNamesAHub_LinksTheHubAndMintsNoBareCarvanaRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        const string vin = "1HGCM82633A004352";
        db.Vehicles.Add(new VehicleEntity { Vin = vin, Year = 2020, Make = "Toyota", Model = "Prius", Mileage = 40000, FirstSeen = FirstWalk, LastSeen = FirstWalk });
        await db.SaveChangesAsync(CancellationToken.None);
        await AddHistoryAsync(db, vin, "Carvana Winder", FirstWalk.AddDays(-4), FirstWalk.AddHours(13));
        RunEntity run = Run(LaterWalk);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(CarvanaFallbackSighting(vin), run, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal("Carvana Winder", dealer.Name);
    }

    [Fact]
    public async Task UpsertAsync_LaterWalkOfAPostingOnTheBareCarvanaRowAndHistoryNamesNoHubForItsWindow_KeepsTheBareRow()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        const string vin = "1HGCM82633A004352";

        RunEntity run1 = Run(FirstWalk);
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run1, CancellationToken.None);
        await AddHistoryAsync(db, vin, "Carvana Fairburn", new DateTimeOffset(2026, 1, 7, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 26, 0, 0, 0, TimeSpan.Zero));

        RunEntity run2 = Run(LaterWalk);
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run2, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Equal("Carvana", dealer.Name);
    }

    [Fact]
    public async Task UpsertAsync_FallbackSightingOfAPostingOnANamedHubWhoseHistoryNamesAnotherHub_KeepsTheHubTheEarlierSightingNamed()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        const string vin = "1HGCM82633A004352";

        RunEntity run1 = Run(FirstWalk);
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate(vin, 18000m, source: "carvana", dealerName: "Carvana Belton"), run1, CancellationToken.None);
        await AddHistoryAsync(db, vin, "Carvana Winder", FirstWalk.AddDays(-4), FirstWalk.AddHours(13));

        RunEntity run2 = Run(LaterWalk);
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(CarvanaFallbackSighting(vin), run2, CancellationToken.None);

        Assert.Equal("Carvana Belton", db.Postings.Include(p => p.Dealer).Single().Dealer!.Name);
        Assert.DoesNotContain(db.Dealers, d => d.Name == "Carvana Winder");
    }

    [Theory]
    [InlineData("Carvana", "Phoenix, AZ")]
    [InlineData("Carvana Winder", "Winder, GA")]
    public async Task UpsertAsync_CarvanaSellerReportedWithACity_IsKeyedByNameAloneSoTwoCitiesShareOneRow(string dealerName, string dealerLocation)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, "https://example.com/a", dealerName: dealerName, dealerLocation: dealerLocation), run, CancellationToken.None);
        await service.UpsertAsync(Candidate("5YJ3E1EA1KF000000", 22000m, "https://example.com/b", dealerName: dealerName, dealerLocation: "Orlando, FL"), run, CancellationToken.None);
        await service.UpsertAsync(Candidate("2T1BURHE0JC000000", 19000m, "https://example.com/c", dealerName: dealerName), run, CancellationToken.None);

        DealerEntity dealer = Assert.Single(db.Dealers);
        Assert.Null(dealer.Location);
        Assert.Equal("", dealer.NormalizedLocation);
        Assert.Equal(3, db.Postings.Count(p => p.DealerId == dealer.Id));
    }

    [Fact]
    public async Task UpsertAsync_NonCarvanaDealerWithACity_StillKeepsItsLocation()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, dealerName: "Carvanaville Motors", dealerLocation: "Sanford, FL"), run, CancellationToken.None);

        Assert.Equal("Sanford, FL", Assert.Single(db.Dealers).Location);
    }

    [Theory]
    [InlineData(1590)]
    [InlineData(0)]
    [InlineData(null)]
    public async Task UpsertAsync_FirstSighting_StoresTheShippingFeeOnThePosting(int? fee)
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow, "walk carvana");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana", shippingFee: fee), run, CancellationToken.None);

        Assert.Equal(fee, (int?)db.Postings.Single().ShippingFee);
        Assert.Equal(18000m, db.PriceObservations.Single().Price);
    }

    [Fact]
    public async Task UpsertAsync_SeenAgainWithADifferentFee_KeepsTheLatestFeeAndAppendsNoObservation()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana", shippingFee: 1590m), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        UpsertOutcome outcome = await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana", shippingFee: 0m), run2, CancellationToken.None);

        Assert.False(outcome.PriceChanged);
        Assert.Equal(0m, db.Postings.Single().ShippingFee);
        Assert.Single(db.PriceObservations);
    }

    [Fact]
    public async Task UpsertAsync_CandidateWithAFeeStatement_StoresThePostureAndTheItemizedTotalOnThePosting()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow, "walk cars.com");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "cars.com") with { FeePosture = FeePostures.Itemized, ItemizedFeesTotal = 1494m }, run, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(FeePostures.Itemized, posting.FeePosture);
        Assert.Equal(1494m, posting.ItemizedFeesTotal);
    }

    [Fact]
    public async Task UpsertAsync_CandidateFromASiteWithNoFeeReader_LeavesThePostureNullRatherThanUnknown()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow, "walk carvana");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana"), run, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Null(posting.FeePosture);
        Assert.Null(posting.ItemizedFeesTotal);
    }

    [Fact]
    public async Task UpsertAsync_SeenAgainWithANewFeeStatement_ReplacesTheEarlierOne()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "walk cars.com");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "cars.com") with { FeePosture = FeePostures.Itemized, ItemizedFeesTotal = 1494m }, run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "walk cars.com");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "cars.com") with { FeePosture = FeePostures.AllIn }, run2, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(FeePostures.AllIn, posting.FeePosture);
        Assert.Null(posting.ItemizedFeesTotal);
    }

    [Fact]
    public async Task UpsertAsync_SeenAgainWithNoFee_ReplacesTheEarlierFeeWithNull()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana", shippingFee: 1590m), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carvana"), run2, CancellationToken.None);

        Assert.Null(db.Postings.Single().ShippingFee);
    }

    [Fact]
    public async Task UpsertAsync_FirstSightingWithAPickupOption_StoresItBesideTheShippingFee()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow, "walk carvana");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 17990m, source: "carvana", shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL"), run, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(990m, posting.ShippingFee);
        Assert.Equal(0m, posting.PickupFee);
        Assert.Equal("Orlando, FL", posting.PickupLocation);
    }

    [Fact]
    public async Task UpsertAsync_SeenAgainWithABlockThatDidNotRender_ReplacesThePickupOptionWithNullAndKeepsTheShippingFee()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 17990m, source: "carvana", shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "walk carvana");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 17990m, source: "carvana", shippingFee: 990m), run2, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(990m, posting.ShippingFee);
        Assert.Null(posting.PickupFee);
        Assert.Null(posting.PickupLocation);
    }

    [Fact]
    public async Task UpsertAsync_FirstSighting_StoresThePickupLocationBesideAZeroFee()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = Run(DateTimeOffset.UtcNow, "walk carmax");
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carmax", shippingFee: 0m, pickupLocation: "Orlando"), run, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(0m, posting.ShippingFee);
        Assert.Equal("Orlando", posting.PickupLocation);
    }

    [Fact]
    public async Task UpsertAsync_SeenAgainAsATransfer_ReplacesThePickupLocationWithNull()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);

        RunEntity run1 = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "walk carmax");
        db.Runs.Add(run1);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carmax", shippingFee: 0m, pickupLocation: "Orlando"), run1, CancellationToken.None);

        RunEntity run2 = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "walk carmax");
        db.Runs.Add(run2);
        await db.SaveChangesAsync(CancellationToken.None);
        await service.UpsertAsync(Candidate("1HGCM82633A004352", 18000m, source: "carmax", shippingFee: 149m), run2, CancellationToken.None);

        PostingEntity posting = db.Postings.Single();
        Assert.Equal(149m, posting.ShippingFee);
        Assert.Null(posting.PickupLocation);
    }
}
