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

    [Theory]
    [InlineData("2022 Toyota Prius LE\n38K miles\n$24,998\n$49 shipping·Get it by Monday\nSave", 24998)]
    [InlineData("2021 Toyota Prius XLE\n52K miles\n$23,498\nAvailable today·Orlando\nSave", 23498)]
    [InlineData("2023 Toyota Prius Prime SE\n21K miles\n$29,998\n$149 shipping·Get it by Sep 28 - Oct 2\nSave", 29998)]
    [InlineData("2020 Toyota Prius Limited\n64K miles\n$21,998\n$1,999 shipping·Get it by Oct 5 - Oct 12\nSave", 21998)]
    public void ReadPrice_OlderCardShapeWithThePriceAheadOfTheFeeLine_ReadsTheFirstAmount(string cardText, int expected)
    {
        Assert.Equal(expected, CarMaxCards.ReadPrice(cardText));
    }

    [Theory]
    [InlineData("View more\nCompare\n2025 Toyota Camry\nSE\n·\n27K mi\nAvailable today·Orlando\nEst. $485/mo\n·\n$29,998", 29998)]
    [InlineData("View more\nCompare\n2021 Toyota Camry Hybrid\nXSE\n·\n25K mi\n$49 shipping·Get it by Wednesday\nEst. $522/mo\n·\n$31,998", 31998)]
    [InlineData("View more\nCompare\n2016 Toyota Camry Hybrid\nXLE\n·\n74K mi\n$499 shipping·Get it by Oct 4 - Oct 10\nEst. $311/mo\n·\n$19,998", 19998)]
    public void ReadPrice_NewerCardShapeWithThePriceAfterTheFeeAndEstimateLines_ReadsTheLastAmount(string cardText, int expected)
    {
        Assert.Equal(expected, CarMaxCards.ReadPrice(cardText));
    }

    [Fact]
    public void ReadPrice_PriceDropCard_ReadsTheCurrentPriceAheadOfTheOldOne()
    {
        // "Price drop" cards print the current (lower) price first and the old one it dropped from second.
        const string card = "Price drop\nView more\nCompare\n2026 Toyota Camry\nSE\n·\n9K mi\n$49 shipping·Get it by Sep 29 - Oct 3\nEst. $522/mo\n·\n$31,998\n$32,998";

        Assert.Equal(31998m, CarMaxCards.ReadPrice(card));
    }

    [Fact]
    public void ReadPrice_ShippingAmountAndMonthlyEstimateAreNeverMistakenForThePrice()
    {
        Assert.Equal(29998m, CarMaxCards.ReadPrice("$29,998\n$149 shipping·Get it by Sep 28 - Oct 2"));
        Assert.Equal(19998m, CarMaxCards.ReadPrice("$499 shipping·Get it by Oct 4 - Oct 10\nEst. $311/mo\n·\n$19,998"));
    }

    [Theory]
    [InlineData("$49 shipping·Get it by Monday")]
    [InlineData("Est. $311/mo")]
    [InlineData("")]
    public void ReadPrice_CardWithNoPriceLeft_ReturnsNull(string cardText)
    {
        Assert.Null(CarMaxCards.ReadPrice(cardText));
    }
}
