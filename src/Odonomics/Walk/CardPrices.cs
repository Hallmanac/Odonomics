using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads a result card's asking price off the card's own text, for the sites whose card
/// shape has been confirmed from a recorded search page. Each reader takes the card's visible text
/// and returns null when it shows no price it can read, so a card is never given a made-up figure.</summary>
public static class CardPrices
{
    private const NumberStyles AmountStyles = NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;

    private static readonly Regex DollarAmount = new(@"\$\s*(?<amount>\d[\d,]*(?:\.\d+)?)", RegexOptions.Compiled);

    private static readonly Regex CarvanaCurrentPriceLine = new(
        @"Current\s+price:\s*\$\s*(?<amount>\d[\d,]*(?:\.\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A CarGurus price is a line of its own, which keeps a price-drop amount ("-$539") and a monthly estimate
    // ("$373/mo est.") from reading as the price.
    private static readonly Regex CarGurusPriceLine = new(
        @"^[ \t]*\$(?<amount>\d[\d,]*)[ \t\r]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // autotrader prints its card price as bare digits, immediately before "See payment" (no dollar
    // sign anywhere near it), which is also what WalkSites.Autotrader's own CardAmountPattern override
    // tells SearchPageLinks.CardScript to treat as this card's price marker.
    private static readonly Regex AutotraderPriceBeforeSeePayment = new(
        @"(?<amount>\d[\d,]*)\s+See payment",
        RegexOptions.Compiled);

    /// <summary>The first dollar amount in the card's text. A cars.com card reads its price, then a
    /// price-drop amount when it has one, then its mileage and its "Used 2023 ..." title, so the
    /// first amount is the asking price.</summary>
    public static decimal? FirstDollarAmount(string cardText) => Read(DollarAmount.Match(cardText));

    /// <summary>The amount after "Current price:" on a carvana card. A marked-down card goes on to
    /// print "Original price: was $..." and a monthly estimate, neither of which is the asking price,
    /// so a card with no "Current price:" reads as no price.</summary>
    public static decimal? CarvanaCurrentPrice(string cardText) => Read(CarvanaCurrentPriceLine.Match(cardText));

    /// <summary>The last line of a CarGurus card that is a dollar amount and nothing else. A price-drop card prints the old
    /// price and then the current one as two such lines, after a "-$N" drop amount that is not read; the card goes on to
    /// print "Price includes fees" and a monthly estimate, which are not dollar-only lines either. The price is the
    /// delivered one, with any shipping the card names already in it.</summary>
    public static decimal? CarGurusPrice(string cardText) =>
        Read(CarGurusPriceLine.Matches(cardText).LastOrDefault());

    /// <summary>The bare-digit amount right before "See payment" on an autotrader card. A "Consider
    /// Buying New" recommendation card prints "See details" instead of "See payment" and so reads no
    /// price here; a "New ... MSRP$" one does carry "See payment" after its MSRP figure and so would
    /// read that as a price, but neither is ever a candidate anyway (its link carries no
    /// clickType=listing, see <see cref="WalkSite.ResultCardLinkPattern"/>).</summary>
    public static decimal? AutotraderCardPrice(string cardText) => Read(AutotraderPriceBeforeSeePayment.Match(cardText));

    private static decimal? Read(Match? match) =>
        match is { Success: true }
            ? decimal.Parse(match.Groups["amount"].Value, AmountStyles, CultureInfo.InvariantCulture)
            : null;
}
