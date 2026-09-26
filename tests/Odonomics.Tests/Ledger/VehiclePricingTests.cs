using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class VehiclePricingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();

    private static PostingEntity Posting(string source, decimal price, decimal? shippingFee = null, DateTimeOffset? lastSeen = null, decimal? pickupFee = null, string? pickupLocation = null, string? feePosture = null, decimal? itemizedFees = null) => new()
    {
        VehicleVin = "1HGCM82633A004352",
        Source = source,
        Url = $"https://example.com/{source}",
        FirstSeen = RunTime,
        LastSeen = lastSeen ?? RunTime,
        ShippingFee = shippingFee,
        PickupFee = pickupFee,
        PickupLocation = pickupLocation,
        FeePosture = feePosture,
        ItemizedFeesTotal = itemizedFees,
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
    public void LowestCurrentPurchasePrice_PostingWithAFee_AddsTheFeeToTheAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(Posting("carvana", 16410m, shippingFee: 1590m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(new PurchasePrice(16410m, 1590m), price);
        Assert.Equal(18000m, price?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UnderDelivery_AddsTheShippingFeeAndIgnoresAKnownPickupFee()
    {
        VehicleEntity vehicle = Vehicle(Posting("carvana", 17990m, shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL"));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(18980m, price?.Total);
        Assert.Equal(Fulfillment.Delivery, price?.Fulfillment);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UnderPickup_AddsTheKnownPickupFeeInsteadOfTheShippingFee()
    {
        VehicleEntity vehicle = Vehicle(Posting("carvana", 17990m, shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL"));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Pickup);

        Assert.Equal(17990m, price?.Total);
        Assert.Equal(new PurchasePrice(17990m, 990m, 0m, "Orlando, FL", Fulfillment.Pickup), price);
        Assert.False(price?.PickupFeeAssumed);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UnderPickupWithNoPickupFeeRead_TreatsTheShippingFeeAsThePickupFee()
    {
        VehicleEntity vehicle = Vehicle(Posting("carvana", 17990m, shippingFee: 990m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Pickup);

        Assert.Equal(18980m, price?.Total);
        Assert.True(price?.PickupFeeAssumed);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UnderPickupWithNoFeeAnywhere_IsTheAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17990m));

        Assert.Equal(17990m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Pickup)?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_DearerCarvanaCarThatPicksUpFree_WinsOnlyUnderPickup()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 17500m, shippingFee: 990m, pickupFee: 0m, pickupLocation: "Orlando, FL"),
            Posting("cars.com", 18000m));

        Assert.Equal(18000m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery)?.Total);
        Assert.Equal(17500m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Pickup)?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_PostingWithNoFee_IsTheAskingPriceAsBefore()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 16410m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(16410m, price?.Total);
        Assert.Equal(VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage), price?.Asking);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_FarAwayCarvanaCarAgainstADearerLocalOne_PicksTheLocalOne()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 1590m),
            Posting("cars.com", 17000m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(new PurchasePrice(17000m, null), price);
        Assert.Equal(16000m, VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_FreeShippingBeatsAFeeOnAnEqualAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 290m),
            Posting("cars.com", 16000m, shippingFee: 0m));

        Assert.Equal(new PurchasePrice(16000m, 0m), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_TiedPurchasePrices_ReportsTheLowerAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 1000m),
            Posting("cars.com", 17000m));

        Assert.Equal(new PurchasePrice(16000m, 1000m), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_PostingTheLatestRunNoLongerSaw_IsIgnoredWithItsFee()
    {
        DateTimeOffset laterRun = RunTime.AddDays(1);
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 15000m, shippingFee: 290m),
            Posting("cars.com", 17000m, lastSeen: laterRun));
        var coverage = new Dictionary<string, DateTimeOffset>
        {
            [RunSources.Key("carvana", "Prius")] = laterRun,
            [RunSources.Key("cars.com", "Prius")] = laterRun,
        };

        Assert.Equal(new PurchasePrice(17000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
    }

    private static RunEntity WalkRun(DateTimeOffset startedAt, bool capped)
    {
        string token = RunSources.Key("cars.com", "Prius");
        return new RunEntity { Command = "walk", StartedAt = startedAt, Sources = capped ? $"{token},{RunSources.PartialKey(token)}" : token };
    }

    [Fact]
    public void LowestCurrentPurchasePrice_CappedWalkThatMissedThePosting_KeepsThePostingListedFromTheFullWalk()
    {
        DateTimeOffset fullWalk = RunTime;
        DateTimeOffset cappedWalk = RunTime.AddDays(1);
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 15000m, lastSeen: fullWalk));
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource([WalkRun(fullWalk, capped: false), WalkRun(cappedWalk, capped: true)]);

        Assert.Equal(new PurchasePrice(15000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
        Assert.Equal(15000m, VehiclePricing.LowestCurrentPrice(vehicle, coverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_CappedWalkThatReachedThePosting_CountsIt()
    {
        DateTimeOffset fullWalk = RunTime;
        DateTimeOffset cappedWalk = RunTime.AddDays(1);
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 14000m, lastSeen: cappedWalk), Posting("cars.com", 15000m, lastSeen: fullWalk));
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource([WalkRun(fullWalk, capped: false), WalkRun(cappedWalk, capped: true)]);

        Assert.Equal(new PurchasePrice(14000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_FullWalkAfterTheCappedOneThatDidNotSeeThePosting_DropsIt()
    {
        DateTimeOffset fullWalk = RunTime;
        DateTimeOffset cappedWalk = RunTime.AddDays(1);
        DateTimeOffset laterFullWalk = RunTime.AddDays(2);
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 15000m, lastSeen: fullWalk));
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(
            [WalkRun(fullWalk, capped: false), WalkRun(cappedWalk, capped: true), WalkRun(laterFullWalk, capped: false)]);

        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_PostingTheCappedWalkDidNotReachButAFullWalkBeforeItSaw_IsStillListedAfterTwoCappedWalks()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 15000m, lastSeen: RunTime));
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(
            [WalkRun(RunTime, capped: false), WalkRun(RunTime.AddDays(1), capped: true), WalkRun(RunTime.AddDays(2), capped: true)]);

        Assert.Equal(new PurchasePrice(15000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_ItemizedPosting_AddsTheItemizedFeesToTheAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, feePosture: FeePostures.Itemized, itemizedFees: 1494m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(new PurchasePrice(17000m, null, ItemizedFees: 1494m, FeePosture: FeePostures.Itemized), price);
        Assert.Equal(18494m, price?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_ItemizedFeesAndAShippingFee_AddsBoth()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, shippingFee: 500m, feePosture: FeePostures.Itemized, itemizedFees: 1494m));

        Assert.Equal(18994m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery)?.Total);
    }

    [Theory]
    [InlineData(FeePostures.AllIn)]
    [InlineData(FeePostures.Unknown)]
    [InlineData(null)]
    public void LowestCurrentPurchasePrice_AllInUnknownOrUnreadPosture_AddsNothingEvenWithAnItemizedTotalOnTheRow(string? posture)
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, feePosture: posture, itemizedFees: 1494m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(17000m, price?.Total);
        Assert.Null(price?.ItemizedFees);
        Assert.Equal(posture, price?.FeePosture);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_ItemizedPostingWhoseFeesMakeItDearer_LosesToAnAllInPostingWithAHigherAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("cars.com", 17000m, feePosture: FeePostures.Itemized, itemizedFees: 1494m),
            Posting("autotrader", 17800m, feePosture: FeePostures.AllIn));

        Assert.Equal(new PurchasePrice(17800m, null, FeePosture: FeePostures.AllIn), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
        Assert.Equal("autotrader", VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery)?.Source);
    }

    [Fact]
    public void LowestCurrentPurchasePosting_NoPostings_IsNull()
    {
        Assert.Null(VehiclePricing.LowestCurrentPurchasePosting(Vehicle(), NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_NoPostings_IsNull()
    {
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(Vehicle(), NoCoverage, Fulfillment.Delivery));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void LowestCurrentPrice_PostingBelowThePlaceholderFloor_IsIgnoredSoARealPostingWins(int placeholder)
    {
        VehicleEntity vehicle = Vehicle(
            Posting("auto.dev", placeholder),
            Posting("cars.com", 17000m));

        Assert.Equal(17000m, VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
        Assert.Equal(new PurchasePrice(17000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPrice_OnlyActivePostingBelowTheFloor_ReportsNoCurrentPriceLikeNoActivePosting()
    {
        VehicleEntity vehicle = Vehicle(Posting("auto.dev", 0m, shippingFee: 500m));

        Assert.Null(VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPrice_PostingAtTheFloor_StillCounts()
    {
        VehicleEntity vehicle = Vehicle(Posting("auto.dev", PlaceholderPrice.Floor));

        Assert.Equal(PlaceholderPrice.Floor, VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
    }

    [Fact]
    public void LowestCurrentPrice_PostingWhoseLatestObservationIsBelowTheFloor_DoesNotFallBackToAnOlderPrice()
    {
        PostingEntity posting = Posting("auto.dev", 17000m);
        posting.PriceObservations.Add(new PriceObservationEntity { PostingId = 0, Price = 0m, ObservedAt = RunTime.AddDays(1) });

        Assert.Null(VehiclePricing.LowestCurrentPrice(Vehicle(posting), NoCoverage));
    }
}
