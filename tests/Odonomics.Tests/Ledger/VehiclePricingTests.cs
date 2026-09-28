using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Ledger;

public class VehiclePricingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();

    private static PostingEntity Posting(string source, decimal price, decimal? shippingFee = null, DateTimeOffset? lastSeen = null, decimal? pickupFee = null, string? pickupLocation = null, string? feePosture = null, decimal? itemizedFees = null, string? dealerName = null) => new()
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
        Dealer = dealerName is null ? null : new DealerEntity { Name = dealerName, NormalizedName = dealerName.ToUpperInvariant(), NormalizedLocation = "" },
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
    public void LowestCurrentPurchasePrice_CardUnrenderedOnAFullyCoveredPair_KeepsThePostingListed()
    {
        // Unlike a capped walk, a full walk with one exempted posting does advance
        // LatestCoverageBySource past that posting's own LastSeen; CardUnrenderedSeenAt is what
        // keeps it counted active anyway, exactly as a "beyond the cap" posting stays.
        DateTimeOffset firstRun = RunTime;
        DateTimeOffset laterFullRun = RunTime.AddDays(1);
        PostingEntity posting = Posting("cars.com", 15000m, lastSeen: firstRun);
        posting.CardUnrenderedSeenAt = laterFullRun;
        VehicleEntity vehicle = Vehicle(posting);
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(
            [WalkRun(firstRun, capped: false), WalkRun(laterFullRun, capped: false)]);

        Assert.Equal(new PurchasePrice(15000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
        Assert.Equal(15000m, VehiclePricing.LowestCurrentPrice(vehicle, coverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UntouchedPostingWithNoUnrenderedStamp_DropsOnceAFullWalkAdvancesPastIt()
    {
        DateTimeOffset firstRun = RunTime;
        DateTimeOffset laterFullRun = RunTime.AddDays(1);
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 15000m, lastSeen: firstRun));
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(
            [WalkRun(firstRun, capped: false), WalkRun(laterFullRun, capped: false)]);

        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_UnrenderedStampOlderThanTheLatestFullCoverage_DoesNotKeepThePostingListed()
    {
        // The render wait gave up on this posting once (run 2), but a later full run (run 3) read
        // every page without flagging it again: the exemption does not carry forward on its own,
        // the same as an ordinary LastSeen would not.
        DateTimeOffset firstRun = RunTime;
        DateTimeOffset unrenderedRun = RunTime.AddDays(1);
        DateTimeOffset laterFullRun = RunTime.AddDays(2);
        PostingEntity posting = Posting("cars.com", 15000m, lastSeen: firstRun);
        posting.CardUnrenderedSeenAt = unrenderedRun;
        VehicleEntity vehicle = Vehicle(posting);
        Dictionary<string, DateTimeOffset> coverage = RunSources.LatestCoverageBySource(
            [WalkRun(firstRun, capped: false), WalkRun(unrenderedRun, capped: false), WalkRun(laterFullRun, capped: false)]);

        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage, Fulfillment.Delivery));
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
    public void LowestCurrentPurchasePrice_AllInPostingWithListedFees_CarriesThemAsIncludedFeesForDisplayOnly()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, feePosture: FeePostures.AllIn, itemizedFees: 1494m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(1494m, price?.IncludedFees);
        Assert.Equal(17000m, price?.Total);
    }

    [Theory]
    [InlineData(Fulfillment.Delivery)]
    [InlineData(Fulfillment.Pickup)]
    public void LowestCurrentPurchasePrice_AllInPostingThatNamesShipping_ShowsTheFeeAndNeverAddsItUnderEitherFulfillment(Fulfillment fulfillment)
    {
        // cargurus: "Price includes $699 shipping", so the $27,697 asking price already holds the $699.
        VehicleEntity vehicle = Vehicle(Posting("cargurus", 27697m, shippingFee: 699m, feePosture: FeePostures.AllIn));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, fulfillment);

        Assert.Equal(27697m, price?.Total);
        Assert.Equal(699m, price?.ShippingFee);
        Assert.True(price?.ShippingIncluded);
        Assert.Null(price?.AppliedFee);
        Assert.False(price?.PickupFeeAssumed);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_CarsComAllInPostingWithADeliveryFee_StillAddsTheFeeUnderDelivery()
    {
        // Unlike cargurus, cars.com's all-in posture ("Seller has no extra fees") speaks only to the
        // dealer's documentation fees; an ordinary cars.com dealer's own delivery fee (such as "$150
        // delivery from Palmetto Bay, FL") is a separate charge the page never said was in the price,
        // so it is still added.
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 16998m, shippingFee: 1999m, feePosture: FeePostures.AllIn));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(18997m, price?.Total);
        Assert.False(price?.ShippingIncluded);
    }

    [Fact]
    public void ForScoring_AllInPostingThatNamesShipping_ScoresAtTheAskingPriceAlone()
    {
        VehicleEntity vehicle = Vehicle(Posting("cargurus", 27697m, shippingFee: 699m, feePosture: FeePostures.AllIn));

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, "32833", 50);

        Assert.True(forScoring.ShippingIncluded);
        Assert.Equal(699m, forScoring.ShippingFee);
        Assert.Equal(27697m, forScoring.PurchasePrice?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_AllInPostingWithNoShippingFee_IsNotMarkedAsIncludingShipping()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 17000m, feePosture: FeePostures.AllIn));

        Assert.False(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery)?.ShippingIncluded);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_NeverReadPostingWithAShippingFee_StillAddsIt()
    {
        VehicleEntity vehicle = Vehicle(Posting("carmax", 24998m, shippingFee: 149m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(25147m, price?.Total);
        Assert.False(price?.ShippingIncluded);
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

    private static PostingAttributeEntity AvailabilityAttribute(string value) =>
        new() { PostingId = 0, Name = PostingAttributeNames.Availability, Value = value, ObservedRunId = 1 };

    [Theory]
    [InlineData(CarMaxStores.Reserved)]
    [InlineData(CarMaxStores.ComingSoon)]
    public void OnlyReservedOrInTransit_OneActivePostingCarryingTheNote_IsTrue(string note)
    {
        PostingEntity posting = Posting("carmax", 24998m);
        posting.Attributes = [AvailabilityAttribute(note)];
        VehicleEntity vehicle = Vehicle(posting);

        Assert.True(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyReservedOrInTransit_AnotherActivePostingNotReserved_IsFalse()
    {
        PostingEntity reserved = Posting("carmax", 24998m);
        reserved.Attributes = [AvailabilityAttribute(CarMaxStores.Reserved)];
        PostingEntity available = Posting("cars.com", 26000m);
        VehicleEntity vehicle = Vehicle(reserved, available);

        Assert.False(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyReservedOrInTransit_AttributeClearedByALaterWalk_IsFalse()
    {
        // A later detail visit that finds the reservation lifted removes the attribute row and
        // stamps AvailabilityClearedAt (see LedgerUpsertService.ApplyAttributes); the row's absence,
        // not the timestamp, is what this reads.
        PostingEntity posting = Posting("carmax", 24998m);
        posting.Attributes = [];
        posting.AvailabilityClearedAt = RunTime;
        VehicleEntity vehicle = Vehicle(posting);

        Assert.False(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyReservedOrInTransit_NoActivePostingsAtAll_IsFalse()
    {
        Assert.False(VehiclePricing.OnlyReservedOrInTransit(Vehicle(), NoCoverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_ReservedPostingCheaperThanAnotherLivePosting_PricesFromTheLiveOne()
    {
        PostingEntity reserved = Posting("carmax", 20000m);
        reserved.Attributes = [AvailabilityAttribute(CarMaxStores.Reserved)];
        PostingEntity available = Posting("cars.com", 22000m);
        VehicleEntity vehicle = Vehicle(reserved, available);

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery);
        PostingEntity? posting = VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(22000m, price?.Asking);
        Assert.Equal("cars.com", posting?.Source);
        Assert.False(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyReservedOrInTransit_OtherLivePostingHasNoValidPrice_IsTrueAndPricesNothing()
    {
        PostingEntity reserved = Posting("carmax", 20000m);
        reserved.Attributes = [AvailabilityAttribute(CarMaxStores.Reserved)];
        PostingEntity noValidPrice = Posting("cars.com", 0m);
        VehicleEntity vehicle = Vehicle(reserved, noValidPrice);

        Assert.True(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void ReservedOrInTransitNote_ReservedPostingWithAnotherLivePosting_StillReportsTheNote()
    {
        PostingEntity reserved = Posting("carmax", 20000m);
        reserved.Attributes = [AvailabilityAttribute(CarMaxStores.Reserved)];
        PostingEntity available = Posting("cars.com", 22000m);
        VehicleEntity vehicle = Vehicle(reserved, available);

        Assert.Equal(CarMaxStores.Reserved, VehiclePricing.ReservedOrInTransitNote(vehicle, NoCoverage));
    }

    // Norco, CA is thousands of miles from 32833 (the daughter scenario's own zip, Orlando FL); the
    // Orlando store is a real CarMax store CarMaxStores' own curated table places under 50 miles from
    // it (see CarMaxStoresTests). Neither figure needs to be exact: the two cases these tests pin sit
    // nowhere near the 50-mile boundary itself.
    private const string DaughterZip = "32833";
    private const int DaughterRadiusMiles = 50;

    [Fact]
    public void OnlyAtOutOfRadiusStore_OnlyPostingIsAnOutOfRadiusOnlyAtStore_ReturnsTheStoreAndExcludesTheVehicle()
    {
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Norco");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Equal("Norco", VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
        Assert.False(VehiclePricing.OnlyReservedOrInTransit(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_OnlyPostingIsAnOutOfRadiusOnlyAtClearwaterStore_ReturnsTheStoreAndExcludesTheVehicle()
    {
        // Clearwater is a real Florida CarMax store, about 110 miles from the daughter scenario's own
        // zip (32833, Orlando FL): still out of its 50-mile radius, unlike the in-state Orlando and
        // Daytona stores CarMaxStores also knows.
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Clearwater");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Equal("Clearwater", VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.UnmeasuredOnlyAtStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_OnlyPostingIsAnInRadiusOnlyAtStore_ReturnsNullAndStillRanks()
    {
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Orlando");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Null(VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles);
        Assert.Equal(19998m, price?.Asking);
        Assert.Equal(19998m, price?.Total);
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_AnotherPurchasablePosting_ReturnsNullAndPricesFromTheOtherPosting()
    {
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Norco");
        PostingEntity available = Posting("cars.com", 22000m);
        VehicleEntity vehicle = Vehicle(onlyAt, available);

        Assert.Null(VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles);
        PostingEntity? posting = VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles);
        Assert.Equal(22000m, price?.Asking);
        Assert.Equal("cars.com", posting?.Source);
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_OtherPurchasablePostingIsACarsComCarMaxDealer_StillExcludesTheVehicle()
    {
        // The Norco Insight bug (19XZE4F13KE013612, walk run 2026-09-28): CarMax's own posting says
        // "Only at Norco", thousands of miles out of radius, but cars.com also carried the same car
        // under its "CarMax Norco" dealer name at a cheaper price with no such marker, since cars.com
        // shows CarMax's nationwide inventory as if it delivered anywhere. That cars.com posting must
        // never count as the vehicle's other purchasable posting: CarMax is already walked nationwide
        // on its own, so the vehicle still stands or falls on its own CarMax posting alone.
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Norco");
        PostingEntity carsComCarMaxCopy = Posting("cars.com", 16998m, dealerName: "CarMax Norco");
        VehicleEntity vehicle = Vehicle(onlyAt, carsComCarMaxCopy);

        Assert.Equal("Norco", VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
        Assert.Null(VehiclePricing.LowestCurrentPurchasePosting(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
        // The cars.com CarMax copy never counts at all, so even the raw asking price (which an
        // otherwise-excluded reserved or out-of-radius posting would still contribute) comes only
        // from the vehicle's own CarMax posting, not the cheaper cars.com one.
        Assert.Equal(19998m, VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_OtherPurchasablePostingIsANonCarMaxDealerOnCarsCom_StillPricesFromIt()
    {
        // A genuine, distinct cars.com dealer (not CarMax) still counts normally: only a CarMax
        // dealer on cars.com is excluded.
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Norco");
        PostingEntity realCarsComListing = Posting("cars.com", 22000m, dealerName: "Holler Honda");
        VehicleEntity vehicle = Vehicle(onlyAt, realCarsComListing);

        Assert.Null(VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
        Assert.Equal(22000m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles)?.Asking);
    }

    [Fact]
    public void OnlyCarsComCarMaxPostings_VehiclesOnlyPostingIsACarsComCarMaxCopy_IsTrue()
    {
        // A CarMax car the CarMax walk hasn't reached yet: its only ledger posting is cars.com's own
        // redundant copy, excluded from pricing outright (Ledger.VehiclePricing.ActivePostings), so the
        // vehicle would otherwise read as "every posting is gone" even though it is still listed.
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 16998m, dealerName: "CarMax Norco"));

        Assert.True(VehiclePricing.OnlyCarsComCarMaxPostings(vehicle, NoCoverage));
        Assert.Null(VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyCarsComCarMaxPostings_VehicleAlsoHasItsOwnCarMaxPosting_IsFalse()
    {
        // Once CarMax's own walk reaches the same car, it has a real active posting to price and rank
        // from, so the cars.com copy alongside it no longer makes this true.
        PostingEntity ownCarMaxPosting = Posting("carmax", 19998m, dealerName: "CarMax Norco");
        PostingEntity carsComCopy = Posting("cars.com", 16998m, dealerName: "CarMax Norco");
        VehicleEntity vehicle = Vehicle(ownCarMaxPosting, carsComCopy);

        Assert.False(VehiclePricing.OnlyCarsComCarMaxPostings(vehicle, NoCoverage));
    }

    [Fact]
    public void OnlyCarsComCarMaxPostings_VehicleActuallyGone_IsFalse()
    {
        // A vehicle with no cars.com CarMax posting at all whose only posting has aged out of coverage
        // is genuinely gone, not this shape.
        Dictionary<string, DateTimeOffset> coverage = new() { [RunSources.Key("cars.com", "Prius")] = RunTime.AddDays(1) };
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 22000m, dealerName: "Holler Honda"));

        Assert.False(VehiclePricing.OnlyCarsComCarMaxPostings(vehicle, coverage));
        Assert.Null(VehiclePricing.LowestCurrentPrice(vehicle, coverage));
    }

    [Fact]
    public void OnlyAtOutOfRadiusStore_NoZipOrRadiusGiven_NeverExcludes()
    {
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Norco");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Null(VehiclePricing.OnlyAtOutOfRadiusStore(vehicle, NoCoverage, zip: null, radiusMiles: null));
        Assert.Equal(19998m, VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery)?.Asking);
    }

    [Fact]
    public void LowestPriceIncludingOutOfRadius_OnlyPostingIsAnOutOfRadiusOnlyAtStore_StillPricesIt()
    {
        PostingEntity onlyAt = Posting("carmax", 28000m, shippingFee: 0m, pickupLocation: "Only at Norco");
        VehicleEntity vehicle = Vehicle(onlyAt);

        PurchasePrice? price = VehiclePricing.LowestPriceIncludingOutOfRadius(vehicle, NoCoverage, Fulfillment.Delivery);

        Assert.Equal(28000m, price?.Asking);
        Assert.Equal(28000m, price?.Total);
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles));
    }

    [Fact]
    public void LowestPriceIncludingOutOfRadius_ReservedPosting_StillExcludesIt()
    {
        PostingEntity reserved = Posting("carmax", 15000m);
        reserved.Attributes = [AvailabilityAttribute(CarMaxStores.Reserved)];
        VehicleEntity vehicle = Vehicle(reserved);

        Assert.Null(VehiclePricing.LowestPriceIncludingOutOfRadius(vehicle, NoCoverage, Fulfillment.Delivery));
    }

    [Fact]
    public void UnmeasuredOnlyAtStore_StoreNotInTheCuratedTable_ReturnsTheStore()
    {
        PostingEntity onlyAt = Posting("carmax", 25000m, shippingFee: 0m, pickupLocation: "Only at Nowhere");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Equal("Nowhere", VehiclePricing.UnmeasuredOnlyAtStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
    }

    [Fact]
    public void UnmeasuredOnlyAtStore_StoreInTheCuratedTable_ReturnsNull()
    {
        PostingEntity onlyAt = Posting("carmax", 25000m, shippingFee: 0m, pickupLocation: "Only at Norco");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Null(VehiclePricing.UnmeasuredOnlyAtStore(vehicle, NoCoverage, DaughterZip, DaughterRadiusMiles));
    }

    [Fact]
    public void UnmeasuredOnlyAtStore_NoZipOrRadiusGiven_ReturnsNull()
    {
        PostingEntity onlyAt = Posting("carmax", 25000m, shippingFee: 0m, pickupLocation: "Only at Nowhere");
        VehicleEntity vehicle = Vehicle(onlyAt);

        Assert.Null(VehiclePricing.UnmeasuredOnlyAtStore(vehicle, NoCoverage, zip: null, radiusMiles: null));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_InRadiusOnlyAtPostingUnderDelivery_DoesNotTreatTheZeroShippingFeeAsFree()
    {
        // CarMax's own $0 shipping fee for an "Only at" posting is a side effect of the same
        // "Available today" card line an ordinary local pickup city sets it from, not a real
        // free-delivery offer: CarMax ships nothing for a car like this, only pickup at its one
        // store. Null (no fee at all), not $0, is what stops the detail line from calling it free.
        PostingEntity onlyAt = Posting("carmax", 19998m, shippingFee: 0m, pickupLocation: "Only at Orlando");
        VehicleEntity vehicle = Vehicle(onlyAt);

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage, Fulfillment.Delivery, DaughterZip, DaughterRadiusMiles);

        Assert.Null(price?.ShippingFee);
        Assert.Null(price?.AppliedFee);
        Assert.Equal(19998m, price?.Total);
    }
}
