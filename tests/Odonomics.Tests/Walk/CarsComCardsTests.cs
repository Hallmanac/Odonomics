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
    public void ReadDistanceMiles_MultipleCardsConcatenated_ReadsAsNull()
    {
        // The site's own "nearest ancestor with a dollar amount" heuristic sometimes hands a link a
        // wrapper spanning more than one card (lesson 9514dea8): the first distance in that text belongs
        // to whichever neighboring card happens to lead it, not to this card's own car, so a blob naming
        // more than one distance is read as stating none rather than measured by the first.
        string blob = "$20,494\n\n87,613 mi.\nUsed 2020 Honda Insight EX\n\nBeaver Toyota\n\n4.9\nAugustine, FL (95 mi)\nCheck Availability\n\n$22,985\n\nBuick Lakeland\n\nLakeland, FL (65 mi)";

        Assert.Null(CarsComCards.ReadDistanceMiles(blob));
    }

    [Theory]
    [InlineData("Denver, CO (1,742 mi)", 1742)]
    [InlineData("$249 delivery to Orlando, FL (1,014 mi)", 1014)]
    public void ReadDistanceMiles_ThousandsSeparator_ReadsTheParentheticalMiles(string cardText, int expected)
    {
        Assert.Equal(expected, CarsComCards.ReadDistanceMiles(cardText));
    }

    [Theory]
    [InlineData("$18,998\n\n80,924 mi.\nEst. $345/mo\nUsed 2019 Honda Insight LX\nGood Deal\n\nCarMax Town Center\n\n4.3\n$199 delivery to Orlando, FL (14 mi)\nCheck Availability")]
    [InlineData("$23,998\n\n40,742 mi.\nEst. $436/mo\nUsed 2020 Honda Insight EX\nGreat Deal\n\nCarMax Independence Boulevard\n\n4.1\n$249 delivery to Orlando, FL (14 mi)\nCheck Availability")]
    public void ReadsAsCarMax_RecordedCarMaxDeliveryCards_IsTrue(string cardText)
    {
        Assert.True(CarsComCards.ReadsAsCarMax(cardText));
    }

    [Theory]
    // An ordinary dealer 130 miles or more away who also charges its own delivery fee: the fee reads
    // "delivery from" the seller's own city, never "delivery to" the search's zip, and the card names no
    // CarMax store (lesson 4a2cbaa3, walk runs 20260928-134242 and 20260928-184522).
    [InlineData("$21,419\n\n93,251 mi.\nEst. $389/mo\nUsed 2022 Honda Insight EX\nFair Deal\n\nOgden Motors\n\n4.6\n$1,493 delivery from Berwyn, IL (996 mi)\nCheck Availability")]
    [InlineData("$150 delivery from Palmetto Bay, FL (205 mi)")]
    [InlineData("Sanford, FL (28 mi)")]
    [InlineData("")]
    public void ReadsAsCarMax_OrdinaryDealerCardsWithOrWithoutTheirOwnDeliveryFee_IsFalse(string cardText)
    {
        Assert.False(CarsComCards.ReadsAsCarMax(cardText));
    }

    [Fact]
    public void ReadsAsCarMax_NamesCarMaxAsDealerButStatesNoDeliveryFee_IsStillTrue()
    {
        // The dealer line alone is enough: a CarMax card whose own fee this card's text does not carry
        // (a multi-card wrapper cut short, or a store that has started charging no delivery fee at all)
        // is still recognized by CarMax's own name in the text.
        Assert.True(CarsComCards.ReadsAsCarMax("Used 2020 Honda Insight EX\n\nCarMax Town Center\n\n4.3\nOrlando, FL (5 mi)"));
    }
}
