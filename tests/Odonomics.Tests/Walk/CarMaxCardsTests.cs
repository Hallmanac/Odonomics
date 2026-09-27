using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the fee a carmax card's availability line stores: a transfer's shipping amount, or
/// zero and the city for a car at a nearby store. The strings are the ones the 2026-09-26 probe of a
/// CarMax Prius search read off its cards.</summary>
public class CarMaxCardsTests
{
    [Theory]
    [InlineData("2022 Toyota Prius LE\n38K miles\n$24,998\n$49 shipping·Get it by Monday", 49)]
    [InlineData("$149 shipping·Get it by Sep 28 - Oct 2", 149)]
    [InlineData("2020 Toyota Prius\n$21,998\n$1,999 shipping·Get it by Oct 5", 1999)]
    [InlineData("$49 SHIPPING·Get it by Monday", 49)]
    [InlineData("$49.00 shipping·Get it by Monday", 49)]
    public void ReadFee_TransferCard_ReturnsTheShippingAmountAndNoPickupLocation(string cardText, int expected)
    {
        Assert.Equal(new CardFee(expected), CarMaxCards.ReadFee(cardText));
    }

    [Theory]
    [InlineData("2021 Toyota Prius XLE\n$23,498\nAvailable today·Orlando\nSave", "Orlando")]
    [InlineData("Available today·Winter Park", "Winter Park")]
    [InlineData("Available today · Sanford\nSave", "Sanford")]
    [InlineData("Available today•Kissimmee", "Kissimmee")]
    public void ReadFee_AvailableTodayCard_ReturnsZeroAndTheCityAsThePickupLocation(string cardText, string city)
    {
        Assert.Equal(new CardFee(0m, city), CarMaxCards.ReadFee(cardText));
    }

    [Fact]
    public void ReadFee_ShippingAmountIsNotTheAskingPrice()
    {
        // The asking price is the first dollar amount on the card; the fee is only the one before "shipping".
        Assert.Equal(new CardFee(149m), CarMaxCards.ReadFee("$29,998\n$149 shipping·Get it by Sep 28 - Oct 2"));
    }

    [Theory]
    [InlineData("2022 Toyota Prius LE\n38K miles\n$24,998")]
    [InlineData("Available today")]
    [InlineData("Available today·")]
    [InlineData("")]
    public void ReadFee_CardWithNoAvailabilityText_ReturnsNullRatherThanFree(string cardText)
    {
        Assert.Null(CarMaxCards.ReadFee(cardText));
    }

    [Fact]
    public void ReadVehicleFacets_RecordedBelowFloorCard_ReadsTheYearMileageAndHybridTitle()
    {
        const string card = "View more\nCompare\n2016 Toyota Camry Hybrid\nXLE\n·\n74K mi\n$499 shipping·Get it by Oct 4 - Oct 10\nEst. $311/mo\n·\n$19,998";

        CardVehicleFacets facets = CarMaxCards.ReadVehicleFacets(card);

        Assert.Equal(new CardVehicleFacets(2016, 74000, ModelNamesHybrid: true), facets);
    }

    [Fact]
    public void ReadVehicleFacets_RecordedPostCutoverBaseModelCard_ReadsTheYearAndMileageWithNoHybridTitle()
    {
        const string card = "View more\nCompare\n2025 Toyota Camry\nXSE\n·\n40K mi\nAvailable today·Orlando\nEst. $539/mo\n·\n$32,998";

        CardVehicleFacets facets = CarMaxCards.ReadVehicleFacets(card);

        Assert.Equal(new CardVehicleFacets(2025, 40000, ModelNamesHybrid: false), facets);
    }

    [Theory]
    [InlineData("9K mi", 9000)]
    [InlineData("9k mi", 9000)]
    [InlineData("9K miles", 9000)]
    [InlineData("9.5K mi", 9500)]
    public void ReadVehicleFacets_MileageShorthand_RoundsToWholeMiles(string mileageLine, int expected)
    {
        CardVehicleFacets facets = CarMaxCards.ReadVehicleFacets($"View more\nCompare\n2024 Toyota Camry Hybrid\nLE\n·\n{mileageLine}\n$29,998");

        Assert.Equal(expected, facets.Mileage);
    }

    [Fact]
    public void ReadVehicleFacets_CardWithNoTitleLineOrMileage_ReadsNeitherFigure()
    {
        CardVehicleFacets facets = CarMaxCards.ReadVehicleFacets("Save\n$24,998");

        Assert.Equal(new CardVehicleFacets(null, null, ModelNamesHybrid: false), facets);
    }
}
