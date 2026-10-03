using Odonomics.Auctions;
using Odonomics.Cli;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

public class ShowRendererAuctionLinesTests
{
    private static readonly DateTimeOffset CheckedAt = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AuctionLines_FoundSale_PrintsTheStoredRecord()
    {
        var check = new AuctionCheckEntity
        {
            Vin = "JTDBCMFEXS3070309",
            Outcome = AuctionCheckOutcome.Found,
            CheckedAt = CheckedAt,
            Auction = "Copart",
            LotNumber = "1-55637026",
            SaleDate = new DateOnly(2026, 7, 16),
            SaleDocument = "Salvage certificate (CA)",
            PrimaryDamage = "Side",
            SecondaryDamage = "Front end",
            Acv = 23937m,
            RepairEstimate = 22579m,
            Odometer = 83630,
            SourceUrl = "https://bid.cars/en/lot/1-55637026",
        };

        Assert.Equal(
            [
                "  Copart, lot 1-55637026, sold 2026-07-16 (checked 2026-10-03)",
                "  sale document: Salvage certificate (CA)",
                "  damage: Side, secondary Front end",
                "  ACV: $23,937",
                "  repair estimate: $22,579",
                "  odometer: 83,630",
                "  source: https://bid.cars/en/lot/1-55637026",
            ],
            ShowRenderer.AuctionLines(check));
    }

    [Fact]
    public void AuctionLines_NeverChecked_NamesTheCommand() =>
        Assert.Contains("odo title check", Assert.Single(ShowRenderer.AuctionLines(null)));

    [Fact]
    public void AuctionLines_NotFound_SaysNoSaleWasFound() =>
        Assert.Equal(
            ["  no auction sale found in the public archives (checked 2026-10-03)"],
            ShowRenderer.AuctionLines(new AuctionCheckEntity { Vin = "V", Outcome = AuctionCheckOutcome.NotFound, CheckedAt = CheckedAt }));

    [Fact]
    public void AuctionLines_CouldNotRead_GivesTheReason() =>
        Assert.Equal(
            ["  could not read the archives (checked 2026-10-03): the site showed a captcha or block page"],
            ShowRenderer.AuctionLines(new AuctionCheckEntity { Vin = "V", Outcome = AuctionCheckOutcome.CouldNotRead, CheckedAt = CheckedAt, CouldNotReadReason = "the site showed a captcha or block page" }));
}
