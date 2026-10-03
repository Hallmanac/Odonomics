using Microsoft.EntityFrameworkCore;
using Odonomics.Auctions;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class AuctionChecksTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private const string Vin = "JTDBCMFEXS3070309";

    private static readonly AuctionRecord CorollaHybridSale = new(
        "Copart", "1-55637026", new DateOnly(2026, 7, 16), "Salvage certificate (CA)", "Side", "Front end", 23937m, 22579m, 83630, "https://bid.cars/en/lot/1-55637026");

    private static VehicleEntity Vehicle() => new()
    {
        Vin = Vin,
        Year = 2025,
        Make = "Toyota",
        Model = "Corolla Hybrid",
        Mileage = 83682,
        FirstSeen = Now,
        LastSeen = Now,
    };

    private static AuctionCheckEntity Check(AuctionCheckOutcome outcome, DateTimeOffset checkedAt) => new() { Vin = Vin, Outcome = outcome, CheckedAt = checkedAt };

    [Fact]
    public void NeedsCheck_NeverChecked_IsTrue() => Assert.True(AuctionChecks.NeedsCheck(null, Now));

    [Theory]
    [InlineData(AuctionCheckOutcome.Found)]
    [InlineData(AuctionCheckOutcome.NotFound)]
    public void NeedsCheck_ReadResultWithinSevenDays_IsFalse(AuctionCheckOutcome outcome) =>
        Assert.False(AuctionChecks.NeedsCheck(Check(outcome, Now.AddDays(-6)), Now));

    [Theory]
    [InlineData(AuctionCheckOutcome.Found)]
    [InlineData(AuctionCheckOutcome.NotFound)]
    public void NeedsCheck_ReadResultOlderThanSevenDays_IsTrue(AuctionCheckOutcome outcome) =>
        Assert.True(AuctionChecks.NeedsCheck(Check(outcome, Now.AddDays(-8)), Now));

    [Fact]
    public void NeedsCheck_CouldNotReadJustNow_IsTrue() =>
        Assert.True(AuctionChecks.NeedsCheck(Check(AuctionCheckOutcome.CouldNotRead, Now.AddMinutes(-1)), Now));

    [Fact]
    public void RedFlags_FoundSale_NamesTheSaleDocumentDamageAndDate()
    {
        VehicleEntity vehicle = Vehicle();
        AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.Found, CorollaHybridSale, null), Now);

        RedFlag flag = Assert.Single(AuctionChecks.RedFlags(vehicle.AuctionCheck));

        Assert.Equal("salvage-auction", flag.ShortTag);
        Assert.Equal("sold at salvage auction (Salvage certificate (CA), Side/Front end, 2026-07-16)", flag.Detail);
    }

    [Theory]
    [InlineData(AuctionCheckOutcome.NotFound)]
    [InlineData(AuctionCheckOutcome.CouldNotRead)]
    public void RedFlags_NoSaleFound_RaisesNothing(AuctionCheckOutcome outcome)
    {
        VehicleEntity vehicle = Vehicle();
        AuctionChecks.Apply(vehicle, new AuctionLookupResult(outcome, null, outcome == AuctionCheckOutcome.CouldNotRead ? "captcha" : null), Now);

        Assert.Empty(AuctionChecks.RedFlags(vehicle.AuctionCheck));
        Assert.Empty(AuctionChecks.RedFlags(null));
    }

    [Fact]
    public void SalvageAuction_RecordMissingParts_LeavesThemOut()
    {
        Assert.Equal("sold at salvage auction (Side, 2026-07-16)", RedFlagsEvaluator.SalvageAuction(null, "Side", null, new DateOnly(2026, 7, 16)).Detail);
        Assert.Equal("sold at salvage auction (Salvage certificate (CA))", RedFlagsEvaluator.SalvageAuction("Salvage certificate (CA)", null, null, null).Detail);
        Assert.Equal("sold at salvage auction", RedFlagsEvaluator.SalvageAuction(null, null, null, null).Detail);
    }

    [Fact]
    public async Task Apply_StoresFoundThenKeepsItThroughALaterCouldNotRead()
    {
        using var testDb = new LedgerTestDatabase();
        using (OdonomicsDbContext db = testDb.CreateContext())
        {
            VehicleEntity vehicle = Vehicle();
            db.Vehicles.Add(vehicle);
            AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.Found, CorollaHybridSale, null), Now);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        using (OdonomicsDbContext db = testDb.CreateContext())
        {
            AuctionCheckEntity stored = await db.AuctionChecks.SingleAsync(CancellationToken.None);

            Assert.Equal(AuctionCheckOutcome.Found, stored.Outcome);
            Assert.Equal(Now, stored.CheckedAt);
            Assert.Equal("Copart", stored.Auction);
            Assert.Equal("1-55637026", stored.LotNumber);
            Assert.Equal(new DateOnly(2026, 7, 16), stored.SaleDate);
            Assert.Equal("Salvage certificate (CA)", stored.SaleDocument);
            Assert.Equal("Side", stored.PrimaryDamage);
            Assert.Equal("Front end", stored.SecondaryDamage);
            Assert.Equal(23937m, stored.Acv);
            Assert.Equal(22579m, stored.RepairEstimate);
            Assert.Equal(83630, stored.Odometer);
        }

        using (OdonomicsDbContext db = testDb.CreateContext())
        {
            VehicleEntity vehicle = await db.Vehicles.Include(v => v.AuctionCheck).SingleAsync(CancellationToken.None);
            AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, "the site showed a captcha or block page"), Now.AddDays(8));
            await db.SaveChangesAsync(CancellationToken.None);
        }

        using (OdonomicsDbContext db = testDb.CreateContext())
        {
            AuctionCheckEntity stored = await db.AuctionChecks.SingleAsync(CancellationToken.None);

            Assert.Equal(AuctionCheckOutcome.Found, stored.Outcome);
            Assert.Null(stored.CouldNotReadReason);
            Assert.Equal("1-55637026", stored.LotNumber);
            Assert.Equal("Salvage certificate (CA)", stored.SaleDocument);
            Assert.Equal(Now, stored.CheckedAt);
            Assert.True(AuctionChecks.NeedsCheck(stored, Now.AddDays(8)));
            Assert.Equal("Copart 2026-07-16, Salvage certificate (CA)", AuctionChecks.ExclusionSale(stored));
        }
    }

    [Fact]
    public void Apply_NotFoundAfterFound_ReplacesTheSale()
    {
        VehicleEntity vehicle = Vehicle();
        AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.Found, CorollaHybridSale, null), Now);

        AuctionCheckEntity check = AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.NotFound, null, null), Now.AddDays(8));

        Assert.Null(AuctionChecks.ExclusionSale(check));
        Assert.Equal(Now.AddDays(8), check.CheckedAt);
    }

    [Fact]
    public void Apply_CouldNotReadAfterNotFound_StoresTheReason()
    {
        VehicleEntity vehicle = Vehicle();
        AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.NotFound, null, null), Now);

        AuctionCheckEntity check = AuctionChecks.Apply(vehicle, new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, "captcha"), Now.AddDays(8));

        Assert.Equal(AuctionCheckOutcome.CouldNotRead, check.Outcome);
        Assert.Equal("captcha", check.CouldNotReadReason);
        Assert.Equal(Now.AddDays(8), check.CheckedAt);
    }
}
