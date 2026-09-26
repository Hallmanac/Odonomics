using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves a CarMax detail page's dealer is the store its text names ("CarMax Orlando") and not the
/// bare chain name. The header lines below are cut from the 2026-09-26 acceptance walk's recorded pages,
/// where the car's own store is the first store line and the similar cars further down name other stores.</summary>
public class CarMaxStoresTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Read_RecordedDetailFixture_ReturnsTheStoreItNames()
    {
        ResolvedDealer? store = CarMaxStores.Read(Fixture("carmax-detail-29085801.txt"));

        Assert.Equal(new ResolvedDealer("CarMax Orlando", "Orlando", IsFallback: false), store);
    }

    [Theory]
    [InlineData("Test drive at CarMax Orlando, FL", "CarMax Orlando", "Orlando, FL")]
    [InlineData("Ships from CarMax Sanford, FL", "CarMax Sanford", "Sanford, FL")]
    [InlineData("Ships from CarMax Jacksonville West, FL", "CarMax Jacksonville West", "Jacksonville West, FL")]
    [InlineData("Ships from CarMax Ft. Lauderdale, FL", "CarMax Ft. Lauderdale", "Ft. Lauderdale, FL")]
    [InlineData("Available at CarMax Daytona", "CarMax Daytona", "Daytona")]
    public void Read_StoreLine_ReturnsTheStoreAndItsCity(string line, string expectedName, string expectedLocation)
    {
        string page = $"2025 Toyota Camry\nSE\n14k miles\n\n$30,998\nAvailable today\n\n{line}\n\nDetailed history report\n";

        ResolvedDealer? store = CarMaxStores.Read(page);

        Assert.Equal(new ResolvedDealer(expectedName, expectedLocation, IsFallback: false), store);
    }

    [Fact]
    public void Read_SimilarCarsAtOtherStoresAfterTheHeader_ReturnsTheCarsOwnStore()
    {
        const string page = """
            2026 Toyota Camry
            SE
            $28,998
            Ships from CarMax Clearwater, FL
            Similar cars
            Test drive today
            CarMax Melbourne
            Test drive at CarMax Tampa, FL
            """;

        Assert.Equal("CarMax Clearwater", CarMaxStores.Read(page)?.Name);
    }

    [Fact]
    public void Read_PageNamingNoStore_ReturnsNull()
    {
        const string page = """
            CarMax
            Shop Cars
            2022 Toyota Prius LE
            38K miles
            $24,998
            Copyright © 2026 CarMax Enterprise Services, LLC
            CarMax Auto Finance
            CarMax Certified quality
            """;

        Assert.Null(CarMaxStores.Read(page));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CarMax")]
    [InlineData("CarMax Orlando")]
    public void ResolveDealer_RecordedPageNamesAStore_StoresTheStoreWhateverTheExtractionReturned(string? extractedName)
    {
        ResolvedDealer dealer = WalkSites.CarMax.ResolveDealer(extractedName, null, Fixture("carmax-detail-29085801.txt"));

        Assert.Equal(new ResolvedDealer("CarMax Orlando", "Orlando", IsFallback: false), dealer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CarMax")]
    public void ResolveDealer_PageNamingNoStore_KeepsTheBareCarMaxWithNoLocation(string? extractedName)
    {
        ResolvedDealer dealer = WalkSites.CarMax.ResolveDealer(extractedName, "Orlando, FL", "CarMax\n2022 Toyota Prius LE\n$24,998\n");

        Assert.Equal(new ResolvedDealer("CarMax", null, IsFallback: true), dealer);
    }

    [Fact]
    public void ResolveDealer_OnASiteWithNoStoreReader_IgnoresTheStoreLine()
    {
        ResolvedDealer dealer = WalkSites.Carvana.ResolveDealer(null, null, "Test drive at CarMax Orlando, FL");

        Assert.Equal(new ResolvedDealer("Carvana", null, IsFallback: true), dealer);
    }

    [Fact]
    public async Task UpsertAsync_KnownPostingUnderBareCarMaxWhoseDetailPageNamesAStore_MovesToTheStore()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        var firstRun = new RunEntity { Command = "walk carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        var secondRun = new RunEntity { Command = "walk carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.AddRange(firstRun, secondRun);
        await db.SaveChangesAsync(CancellationToken.None);

        ListingCandidate Sighting(ResolvedDealer dealer) => new()
        {
            Vin = "JTDACACU8S3046841",
            Source = "carmax",
            Url = "https://www.carmax.com/car/29085801",
            Year = 2022,
            Make = "Toyota",
            Model = "Prius",
            Price = 24998m,
            Mileage = 38000,
            DealerName = dealer.Name,
            DealerLocation = dealer.Location,
            DealerNameIsFallback = dealer.IsFallback,
        };

        await service.UpsertAsync(Sighting(WalkSites.CarMax.ResolveDealer(null, null, "CarMax\n2022 Toyota Prius LE\n")), firstRun, CancellationToken.None);
        Assert.Equal("CarMax", db.Postings.Include(p => p.Dealer).Single().Dealer!.Name);

        await service.UpsertAsync(Sighting(WalkSites.CarMax.ResolveDealer(null, null, Fixture("carmax-detail-29085801.txt"))), secondRun, CancellationToken.None);

        PostingEntity posting = db.Postings.Include(p => p.Dealer).Single();
        Assert.Equal("CarMax Orlando", posting.Dealer!.Name);
        Assert.Equal("Orlando", posting.Dealer.Location);
    }
}
