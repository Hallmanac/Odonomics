using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class VehiclePricingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();

    private static PostingEntity Posting(string source, decimal price, decimal? shippingFee = null, DateTimeOffset? lastSeen = null) => new()
    {
        VehicleVin = "1HGCM82633A004352",
        Source = source,
        Url = $"https://example.com/{source}",
        FirstSeen = RunTime,
        LastSeen = lastSeen ?? RunTime,
        ShippingFee = shippingFee,
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

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage);

        Assert.Equal(new PurchasePrice(16410m, 1590m), price);
        Assert.Equal(18000m, price?.Total);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_PostingWithNoFee_IsTheAskingPriceAsBefore()
    {
        VehicleEntity vehicle = Vehicle(Posting("cars.com", 16410m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage);

        Assert.Equal(16410m, price?.Total);
        Assert.Equal(VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage), price?.Asking);
    }

    [Fact]
    public void LowestCurrentPurchasePrice_FarAwayCarvanaCarAgainstADearerLocalOne_PicksTheLocalOne()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 1590m),
            Posting("cars.com", 17000m));

        PurchasePrice? price = VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage);

        Assert.Equal(new PurchasePrice(17000m, null), price);
        Assert.Equal(16000m, VehiclePricing.LowestCurrentPrice(vehicle, NoCoverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_FreeShippingBeatsAFeeOnAnEqualAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 290m),
            Posting("cars.com", 16000m, shippingFee: 0m));

        Assert.Equal(new PurchasePrice(16000m, 0m), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_TiedPurchasePrices_ReportsTheLowerAskingPrice()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("carvana", 16000m, shippingFee: 1000m),
            Posting("cars.com", 17000m));

        Assert.Equal(new PurchasePrice(16000m, 1000m), VehiclePricing.LowestCurrentPurchasePrice(vehicle, NoCoverage));
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

        Assert.Equal(new PurchasePrice(17000m, null), VehiclePricing.LowestCurrentPurchasePrice(vehicle, coverage));
    }

    [Fact]
    public void LowestCurrentPurchasePrice_NoPostings_IsNull()
    {
        Assert.Null(VehiclePricing.LowestCurrentPurchasePrice(Vehicle(), NoCoverage));
    }
}
