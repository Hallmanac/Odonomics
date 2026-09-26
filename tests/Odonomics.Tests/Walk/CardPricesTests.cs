using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Card texts here are cut from the search pages the walk recorded on 2026-09-26
/// (walks/cars.com/20260926-121057/corolla-hybrid/search.txt and the carvana one beside it).</summary>
public class CardPricesTests
{
    private const string CarsComCardWithPriceDrop = """
        $21,202

        $349
        48,278 mi.
        Est. $385/mo
        Used 2023 Toyota Corolla Hybrid LE
        Great Deal
        High Demand

        Sport Mazda South

        4.9
        Orlando, FL (21 mi)
        Check Availability
        """;

    private const string CarsComCardWithoutPriceDrop = """
        $22,075

        45,129 mi.
        Est. $401/mo
        Used 2021 Toyota Corolla Hybrid SE
        Good Deal

        AutoNation USA Sanford

        3.3
        Sanford, FL (26 mi)
        Check Availability
        """;

    private const string CarvanaCard = """
        2024 Toyota Corolla Hybrid

        LE

        46k miles
        Current price:
        $24,590
        $440/mo
        estimated
        $0 cash down
        Free shipping
        Get it tomorrow
        """;

    private const string CarvanaMarkedDownCard = """
        Price Drop

        2025 Toyota Corolla Hybrid

        LE

        32k miles
        Current price:
        $25,990
        Original price:
        was
        $26,590
        $465/mo
        estimated
        $0 cash down
        $690 shipping
        Get it Monday
        """;

    [Fact]
    public void CarsCom_ReadsThePriceThatComesBeforeAPriceDropAmountAndTheMileage()
    {
        Assert.Equal(21202m, WalkSites.CarsCom.ReadCardPrice(CarsComCardWithPriceDrop));
    }

    [Fact]
    public void CarsCom_ReadsThePriceOfACardWithNoPriceDrop()
    {
        Assert.Equal(22075m, WalkSites.CarsCom.ReadCardPrice(CarsComCardWithoutPriceDrop));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Used 2021 Toyota Corolla Hybrid SE\n45,129 mi.")]
    public void CarsCom_ACardWithNoDollarAmountHasNoPrice(string cardText)
    {
        Assert.Null(WalkSites.CarsCom.ReadCardPrice(cardText));
    }

    [Fact]
    public void Carvana_ReadsTheAmountAfterCurrentPrice()
    {
        Assert.Equal(24590m, WalkSites.Carvana.ReadCardPrice(CarvanaCard));
    }

    [Fact]
    public void Carvana_AMarkedDownCardReadsItsCurrentPriceNotTheOriginalOne()
    {
        Assert.Equal(25990m, WalkSites.Carvana.ReadCardPrice(CarvanaMarkedDownCard));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2024 Toyota Corolla Hybrid\nLE\n46k miles\n$440/mo\nestimated")]
    [InlineData("Original price:\nwas\n$26,590")]
    public void Carvana_ACardWithNoCurrentPriceHasNoPrice(string cardText)
    {
        Assert.Null(WalkSites.Carvana.ReadCardPrice(cardText));
    }

    [Fact]
    public void Autotrader_HasNoCardPriceReaderUntilItsCardShapeIsConfirmed()
    {
        Assert.Null(WalkSites.Autotrader.CardPriceReader);
        Assert.Null(WalkSites.Autotrader.ReadCardPrice("Used\n2022 Toyota Prius\nLimited\n138K mi\n Hybrid\n17,499\nSee payment"));
    }
}
