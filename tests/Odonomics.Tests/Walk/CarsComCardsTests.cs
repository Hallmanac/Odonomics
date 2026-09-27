using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class CarsComCardsTests
{
    [Theory]
    [InlineData("Sanford, FL (28 mi)", 28)]
    [InlineData("St Augustine, FL (96 mi)", 96)]
    [InlineData("$199 delivery to Orlando, FL (14 mi)", 14)]
    [InlineData("$249 delivery to Orlando, FL (14 mi)", 14)]
    public void ReadDistanceMiles_OrdinaryAndCarMaxDeliveryForms_ReadTheParentheticalMiles(string cardText, int expected)
    {
        Assert.Equal(expected, CarsComCards.ReadDistanceMiles(cardText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Used 2020 Honda Insight EX\nGreat Deal\n\nSome Dealer\n\n4.1")]
    public void ReadDistanceMiles_EmptyOrNoDistanceLine_ReadsAsNull(string cardText)
    {
        Assert.Null(CarsComCards.ReadDistanceMiles(cardText));
    }

    [Fact]
    public void ReadDistanceMiles_MultipleCardsConcatenated_ReadsTheFirstOne()
    {
        // The site's own "nearest ancestor with a dollar amount" heuristic sometimes hands a link a
        // wrapper spanning more than one card; the first distance in the text is still this card's own,
        // since every card prints its price before its distance.
        string blob = "$20,494\n\n87,613 mi.\nUsed 2020 Honda Insight EX\n\nBeaver Toyota\n\n4.9\nAugustine, FL (95 mi)\nCheck Availability\n\n$22,985\n\nBuick Lakeland\n\nLakeland, FL (65 mi)";

        Assert.Equal(95, CarsComCards.ReadDistanceMiles(blob));
    }
}
