using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads how far a cars.com result card says its car is from the search's zip. An ordinary
/// dealer's card prints it as "<c>&lt;City&gt;, &lt;ST&gt; (N mi)</c>" ("Sanford, FL (28 mi)"), and a CarMax
/// listing carried on cars.com's own search prints it the same way after its delivery fee ("$249 delivery
/// to Orlando, FL (14 mi)"), so one pattern reads both. A card with no such line, whether it shows nothing
/// at all (the site's own nationwide recommendation links padding a later page) or some other text
/// entirely, states no distance. So does a card whose text names more than one: the site's bounding
/// heuristic sometimes hands a link the whole multi-card wrapper as its "own" text (an empty-text
/// nationwide link included), and that text's first distance belongs to whichever neighboring card
/// happens to lead it, not to this card's own car.</summary>
public static class CarsComCards
{
    /// <summary>A distance line's own shape ("(28 mi)"), as a JavaScript regular expression source with
    /// no capture group: <see cref="SearchPageLinks.CardScript"/> only needs to count how many a
    /// wrapper's text names, not read one, to tell a multi-card wrapper from a single card's own text (see
    /// <see cref="WalkSite.CardMultiCardPattern"/>).</summary>
    public const string DistanceLinePattern = @"\(\d[\d,]*\s*mi\)";

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
}
