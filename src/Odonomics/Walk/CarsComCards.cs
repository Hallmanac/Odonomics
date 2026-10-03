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

    /// <summary>A cars.com card that states plainly it has no price: its price line reads "Not Priced" (recorded
    /// card for JTDBCMFE5S3085848: "Not Priced / 62,900 mi. / Est. $0/mo / Used 2025 Toyota Corolla Hybrid LE"; the
    /// same for 4T1F31AK8LU544312), or "No Price Listed" or "Call for price" as the other sites word it. Such a
    /// card is no candidate for a detail visit, whose page only repeats "Not Priced" and is dropped as missing its
    /// price, so <see cref="WalkSite.NoPriceCardPattern"/> drops it at the search page first. The line stands alone,
    /// so a priced card that happens to mention one of these phrases in a longer line never matches.</summary>
    public static readonly Regex NoPriceStated = new(
        @"^[ \t]*(?:Not Priced|No Price Listed|Call (?:for|us for) (?:a )?price)[ \t\r]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

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

    private static readonly Regex DeliveryToLine = new(@"\bdelivery\s+to\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Whether <paramref name="cardText"/> reads as one of CarMax's own nationwide-inventory
    /// listings carried on cars.com's own search, rather than an ordinary dealer's card that also happens
    /// to charge its own delivery fee ("$150 delivery from Palmetto Bay, FL (205 mi)" is still an ordinary
    /// dealer, 130 miles or more away every time it has turned up in a recorded walk). <see cref="ReadFee"/>
    /// alone cannot tell the two apart, since both print "$N delivery"; a CarMax card is the one that either
    /// names CarMax as the dealer right in its own text or reads its fee as delivery <em>to</em> the
    /// search's own zip rather than <em>from</em> the seller's (lesson 4a2cbaa3).</summary>
    public static bool ReadsAsCarMax(string cardText) =>
        DeliveryToLine.IsMatch(cardText) || cardText.Contains(WalkSites.CarMaxDealerName, StringComparison.OrdinalIgnoreCase);
}
