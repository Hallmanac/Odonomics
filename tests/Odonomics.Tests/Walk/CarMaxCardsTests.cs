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
}
