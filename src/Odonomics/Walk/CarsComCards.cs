using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads how far a cars.com result card says its car is from the search's zip, and the delivery fee a
/// CarMax listing carried on cars.com's own search states there too. An ordinary dealer's card prints the
/// distance as "<c>&lt;City&gt;, &lt;ST&gt; (N mi)</c>" ("Sanford, FL (28 mi)"), and a CarMax
/// listing carried on cars.com's own search prints it the same way after its delivery fee ("$249 delivery
/// to Orlando, FL (14 mi)"), so one pattern reads both. A card with no such line, whether it shows nothing
/// at all (the site's own nationwide recommendation links padding a later page) or some other text
/// entirely, states no distance. So does a card whose text names more than one: the site's bounding
/// heuristic sometimes hands a link the whole multi-card wrapper as its "own" text (an empty-text
/// nationwide link included), and that text's first distance belongs to whichever neighboring card
/// happens to lead it, not to this card's own car.</summary>
public static class CarsComCards
{
    private static readonly Regex DistanceLine = new(@"\((?<mi>[\d,]+)\s*mi\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The distance in miles <paramref name="cardText"/> states, or null when it states none or
    /// states more than one.</summary>
    public static int? ReadDistanceMiles(string cardText)
    {
        MatchCollection matches = DistanceLine.Matches(cardText);
        return matches.Count == 1 && int.TryParse(matches[0].Groups["mi"].Value.Replace(",", ""), out int miles)
            ? miles
            : null;
    }

    private static readonly Regex DeliveryFeeLine = new(@"\$\s*(?<fee>\d[\d,]*(?:\.\d{2})?)\s+delivery\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The one-time delivery fee a card states ("$249 delivery to Orlando, FL (14 mi)"), wrapped as
    /// a <see cref="CardFee"/> so <see cref="WalkSite.CardFeeReader"/> can read it the same way CarMax's own
    /// card fee is read, and the scenario's price ceiling counts it the same way on cars.com as on every
    /// other site. Null when the card states no such fee, so a car whose card is silent about one is never
    /// treated as free.</summary>
    public static CardFee? ReadFee(string cardText)
    {
        Match match = DeliveryFeeLine.Match(cardText);
        return match.Success
            ? new CardFee(decimal.Parse(match.Groups["fee"].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture))
            : null;
    }
}
