using Odonomics.Auctions;

namespace Odonomics.Tests.Auctions;

public class AuctionPageParserTests
{
    private static AuctionPageReading ParseCorollaHybrid(string fixture, string title = "2025 Toyota Corolla JTDBCMFEXS3070309 - BidCars") =>
        AuctionPageParser.Parse(AuctionFixtures.CorollaHybridVin, AuctionFixtures.CorollaHybridLotUrl, title, AuctionFixtures.Read(fixture));

    [Fact]
    public void Parse_CorollaHybridLotPage_ReadsEveryField()
    {
        AuctionPageReading reading = ParseCorollaHybrid("bid-cars-lot-JTDBCMFEXS3070309.txt");

        Assert.Equal(AuctionPageStatus.Found, reading.Status);
        Assert.Equal(
            new AuctionRecord(
                Auction: "Copart",
                LotNumber: "1-55637026",
                SaleDate: new DateOnly(2026, 7, 16),
                SaleDocument: "Salvage certificate (CA)",
                PrimaryDamage: "Side",
                SecondaryDamage: "Front end",
                Acv: 23937m,
                RepairEstimate: 22579m,
                Odometer: 83630,
                SourceUrl: AuctionFixtures.CorollaHybridLotUrl),
            reading.Record);
    }

    [Fact]
    public void Parse_RecordedPageForAnotherVin_ReadsThatVinsRecordWhenAskedForIt()
    {
        AuctionPageReading reading = AuctionPageParser.Parse(
            "JTDKARFU8K3084588",
            "https://bid.cars/en/lot/1-62993674/2019-Toyota-Prius-JTDKARFU8K3084588",
            "2019 Toyota Prius - BidCars",
            AuctionFixtures.Read("bid-cars-lot-other-vin-for-JTDKARFU6K3085884.txt"));

        AuctionRecord record = Assert.IsType<AuctionRecord>(reading.Record);
        Assert.Equal("Copart", record.Auction);
        Assert.Equal(new DateOnly(2025, 5, 6), record.SaleDate);
        Assert.Equal("Salvage certificate of title (MO)", record.SaleDocument);
        Assert.Equal("All over", record.PrimaryDamage);
        Assert.Null(record.SecondaryDamage);
        Assert.Equal(15835m, record.Acv);
        Assert.Equal(15835m, record.RepairEstimate);
        Assert.Equal(173721, record.Odometer);
    }

    [Fact]
    public void Parse_TabSeparatedAndStackedLabels_ReadsValuesFromTheSameLineOrTheNext()
    {
        AuctionPageReading reading = AuctionPageParser.Parse(
            "2HGFE2F59PH000123",
            "https://bid.cars/en/lot/3-12345678/2023-HONDA-CIVIC",
            "2023 Honda Civic - bid.cars",
            AuctionFixtures.Read("bid-cars-lot-tab-and-stacked-layout.txt"));

        Assert.Equal(AuctionPageStatus.Found, reading.Status);
        AuctionRecord record = Assert.IsType<AuctionRecord>(reading.Record);
        Assert.Equal("IAA", record.Auction);
        Assert.Equal("3-12345678", record.LotNumber);
        Assert.Equal(new DateOnly(2026, 6, 5), record.SaleDate);
        Assert.Equal("Clean title", record.SaleDocument);
        Assert.Equal("Hail", record.PrimaryDamage);
        Assert.Null(record.SecondaryDamage);
        Assert.Equal(21500m, record.Acv);
        Assert.Equal(4200m, record.RepairEstimate);
        Assert.Equal(31022, record.Odometer);
    }

    [Fact]
    public void Parse_RecordedPageForAnotherVin_IsReadableWithNoRecord()
    {
        AuctionPageReading reading = ParseCorollaHybrid("bid-cars-lot-other-vin-for-JTDKARFU6K3085884.txt");

        Assert.Equal(AuctionPageStatus.NoRecord, reading.Status);
        Assert.Null(reading.Record);
    }

    [Fact]
    public void Parse_VinOnlyInTheCompareListOfAnotherLot_IsReadableWithNoRecord()
    {
        // JTDBCMFE5R3056246 appears only under "Compare auctions" on the JTDBCMFEXS3070309 lot page.
        AuctionPageReading reading = AuctionPageParser.Parse(
            "JTDBCMFE5R3056246",
            AuctionFixtures.CorollaHybridLotUrl,
            "2025 Toyota Corolla JTDBCMFEXS3070309 - BidCars",
            AuctionFixtures.Read("bid-cars-lot-JTDBCMFEXS3070309.txt"));

        Assert.Equal(AuctionPageStatus.NoRecord, reading.Status);
        Assert.Null(reading.Record);
    }

    [Fact]
    public void Parse_VinInTheTextButNoLabelledVinField_IsCouldNotReadNotAnotherLotsRecord()
    {
        string page = AuctionFixtures.Read("bid-cars-lot-JTDBCMFEXS3070309.txt")
            .Replace("VIN\nJTDBCMFEXS3070309 \n", "Chassis\nJTDBCMFEXS3070309 \n", StringComparison.Ordinal);

        AuctionPageReading reading = AuctionPageParser.Parse(AuctionFixtures.CorollaHybridVin, AuctionFixtures.CorollaHybridLotUrl, "bid.cars", page);

        Assert.Equal(AuctionPageStatus.CouldNotRead, reading.Status);
        Assert.Null(reading.Record);
        Assert.Contains("not as the lot's own VIN field", reading.Reason);
    }

    [Fact]
    public void Parse_CaptchaPage_IsCouldNotReadWithACaptchaReason()
    {
        AuctionPageReading reading = ParseCorollaHybrid("archive-page-captcha.txt", title: "Just a moment...");

        Assert.Equal(AuctionPageStatus.CouldNotRead, reading.Status);
        Assert.Null(reading.Record);
        Assert.Equal("the site showed a captcha or block page", reading.Reason);
    }

    [Fact]
    public void Parse_NearlyEmptyPage_IsCouldNotReadAsTooShort()
    {
        AuctionPageReading reading = AuctionPageParser.Parse(AuctionFixtures.CorollaHybridVin, AuctionFixtures.CorollaHybridLotUrl, "bid.cars", "Loading...");

        Assert.Equal(AuctionPageStatus.CouldNotRead, reading.Status);
        Assert.Contains("too short", reading.Reason);
    }

    [Fact]
    public void Parse_PageWithTheVinButNoRecognisedFields_IsCouldNotReadNotNotFound()
    {
        AuctionPageReading reading = ParseCorollaHybrid("archive-page-layout-changed.txt");

        Assert.Equal(AuctionPageStatus.CouldNotRead, reading.Status);
        Assert.Contains("layout may have changed", reading.Reason);
        Assert.Contains("bid.cars", reading.Reason);
    }

    [Fact]
    public void Parse_LongLotPageThatMentionsACaptchaInItsFooter_IsStillRead()
    {
        string page = AuctionFixtures.Read("bid-cars-lot-JTDBCMFEXS3070309.txt")
            + string.Concat(Enumerable.Repeat("Related lots and similar vehicles sold recently at auction. ", 40))
            + "This site is protected by a captcha.";

        AuctionPageReading reading = AuctionPageParser.Parse(AuctionFixtures.CorollaHybridVin, AuctionFixtures.CorollaHybridLotUrl, "bid.cars", page);

        Assert.Equal(AuctionPageStatus.Found, reading.Status);
    }

    [Fact]
    public void Parse_PageThatNeverLabelsTheAuction_TakesItFromTheSourceHost()
    {
        string page = AuctionFixtures.Read("bid-cars-lot-tab-and-stacked-layout.txt").Replace("IAA\n", "\n", StringComparison.Ordinal);

        AuctionPageReading reading = AuctionPageParser.Parse("2HGFE2F59PH000123", "https://www.iaai.com/vehicle/3-12345678", "Lot", page);

        Assert.Equal("IAA", Assert.IsType<AuctionRecord>(reading.Record).Auction);
    }
}
