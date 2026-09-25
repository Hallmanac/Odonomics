using Microsoft.EntityFrameworkCore;
using Odonomics.Cli.Commands;
using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Ledger;

/// <summary>Proves a private seller's autotrader listing is stored as one location-less "Private
/// seller" dealer and that `odo dealer grade` never looks that dealer up on CarEdge.</summary>
public class PrivateSellerDealersTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static ListingCandidate PrivateSellerCandidate(string vin, string url, ResolvedDealer dealer) => new()
    {
        Vin = vin,
        Source = "autotrader",
        Url = url,
        Year = 2023,
        Make = "Toyota",
        Model = "Prius",
        Trim = "XLE",
        Price = 32500m,
        Mileage = 21500,
        DealerName = dealer.Name,
        DealerLocation = dealer.Location,
        DealerNameIsFallback = dealer.IsFallback,
    };

    private static ResolvedDealer ResolveFromRecordedPrivatePage(string extractedName, string extractedLocation) =>
        WalkSites.Autotrader.ResolveDealer(
            extractedName,
            extractedLocation,
            File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", "autotrader-detail-private-seller.txt")));

    [Fact]
    public async Task UpsertAsync_TwoPrivateSellersInDifferentCities_ShareOnePrivateSellerDealerWithNoLocation()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        var run = new RunEntity { Command = "walk autotrader", Sources = "", StartedAt = CheckedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        await service.UpsertAsync(PrivateSellerCandidate("JTDADABU1P3005327", "https://www.autotrader.com/cars-for-sale/vehicle/791519242", ResolveFromRecordedPrivatePage("Sample S", "Huntersville, NC")), run, CancellationToken.None);
        await service.UpsertAsync(PrivateSellerCandidate("JTDKAMFU8N3190188", "https://www.autotrader.com/cars-for-sale/vehicle/789082780", ResolveFromRecordedPrivatePage("Another Seller", "Sanford, FL")), run, CancellationToken.None);

        DealerEntity dealer = await db.Dealers.SingleAsync(CancellationToken.None);
        Assert.Equal("Private seller", dealer.Name);
        Assert.Null(dealer.Location);
        Assert.Equal("", dealer.NormalizedLocation);
        Assert.All(await db.Postings.Include(p => p.Dealer).ToListAsync(CancellationToken.None), p => Assert.Same(dealer, p.Dealer));
        Assert.True(PrivateSellerDealers.IsPrivateSeller(dealer));
    }

    [Fact]
    public void SkipReason_PrivateSellerDealer_IsSkippedWithAReason()
    {
        var dealer = new DealerEntity { Name = "Private seller", NormalizedName = DealerNormalizer.Normalize("Private seller"), NormalizedLocation = "" };

        string? reason = DealerGradeCommand.SkipReason(dealer);

        Assert.NotNull(reason);
        Assert.Contains("private seller", reason);
    }

    [Fact]
    public void ApplySkip_PrivateSellerDealer_StampsItCheckedWithNoGradeSoNoRunEverLooksItUp()
    {
        var dealer = new DealerEntity { Name = "Private seller", NormalizedName = "PRIVATE SELLER", NormalizedLocation = "" };
        string reason = DealerGradeCommand.SkipReason(dealer) ?? throw new InvalidOperationException("expected a skip reason");

        DealerGradeOutcome outcome = DealerGradeCommand.ApplySkip(dealer, reason, CheckedAt);

        Assert.Null(dealer.Grade);
        Assert.Equal(reason, dealer.GradeReason);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.True(outcome.Stamped);
        Assert.StartsWith("Private seller: skipped, ", outcome.Line);
    }

    [Theory]
    [InlineData("Private Seller Exchange", "PRIVATE SELLER EXCHANGE")]
    [InlineData("City KIA of Greater Orlando", "CITY KIA OF GREATER ORLANDO")]
    public void SkipReason_AnyOtherDealer_IsLookedUp(string name, string normalizedName)
    {
        var dealer = new DealerEntity { Name = name, NormalizedName = normalizedName, NormalizedLocation = "ORLANDO FL" };

        Assert.Null(DealerGradeCommand.SkipReason(dealer));
    }
}
