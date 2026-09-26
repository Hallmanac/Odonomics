using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class FeeRedFlagsTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();

    private static DealerEntity SeminoleToyota(string? addOnsNote = "$358 add-ons") => new()
    {
        Name = "Seminole Toyota",
        NormalizedName = "SEMINOLE TOYOTA",
        NormalizedLocation = "",
        DocFee = 999m,
        AddOnsNote = addOnsNote,
    };

    private static PostingEntity Posting(string source, decimal price, string? feePosture, DealerEntity? dealer) => new()
    {
        VehicleVin = "1HGCM82633A004352",
        Source = source,
        Url = $"https://example.com/{source}",
        FirstSeen = RunTime,
        LastSeen = RunTime,
        FeePosture = feePosture,
        Dealer = dealer,
        PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = price, ObservedAt = RunTime }],
    };

    private static VehicleEntity Vehicle(params PostingEntity[] postings) => new()
    {
        Vin = "1HGCM82633A004352",
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Mileage = 40000,
        FirstSeen = RunTime,
        LastSeen = RunTime,
        Postings = [.. postings],
    };

    [Fact]
    public void For_CheapestPostingUnknownAtAnAddOnsDealer_RaisesTheFlag()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, FeePostures.Unknown, SeminoleToyota()));

        RedFlag flag = Assert.Single(FeeRedFlags.For(vehicle, NoCoverage, Fulfillment.Delivery));

        Assert.Equal("add-ons-dealer", flag.ShortTag);
        Assert.Contains("Seminole Toyota", flag.Detail);
        Assert.Contains("$999 doc fee", flag.Detail);
    }

    [Theory]
    [InlineData(FeePostures.AllIn)]
    [InlineData(FeePostures.Itemized)]
    [InlineData(null)]
    public void For_CheapestPostingThatIsAllInItemizedOrNeverRead_NeverRaisesIt(string? posture)
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, posture, SeminoleToyota()));

        Assert.Empty(FeeRedFlags.For(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void For_UnknownPostingAtADealerWithNoAddOns_RaisesNothing()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, FeePostures.Unknown, SeminoleToyota("No add-ons")));

        Assert.Empty(FeeRedFlags.For(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void For_UnknownPostingWithNoDealerOrAnUngradedOne_RaisesNothing()
    {
        Assert.Empty(FeeRedFlags.For(Vehicle(Posting("cars.com", 17000m, FeePostures.Unknown, null)), NoCoverage, Fulfillment.Delivery));
        Assert.Empty(FeeRedFlags.For(Vehicle(Posting("cars.com", 17000m, FeePostures.Unknown, SeminoleToyota(addOnsNote: null))), NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void For_ADearerUnknownPostingAtAnAddOnsDealerNextToACheaperAllInOne_RaisesNothingBecauseTheCheapestPostingIsTheOneJudged()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("cars.com", 18000m, FeePostures.Unknown, SeminoleToyota()),
            Posting("autotrader", 17000m, FeePostures.AllIn, SeminoleToyota()));

        Assert.Empty(FeeRedFlags.For(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void For_TheCheapestPostingUnknownAtAnAddOnsDealerNextToADearerAllInOne_RaisesTheFlag()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("cars.com", 17000m, FeePostures.Unknown, SeminoleToyota()),
            Posting("autotrader", 18000m, FeePostures.AllIn, null));

        Assert.Single(FeeRedFlags.For(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void For_VehicleWithNoCurrentPosting_RaisesNothing()
    {
        Assert.Empty(FeeRedFlags.For(Vehicle(), NoCoverage, Fulfillment.Delivery));
    }
}
