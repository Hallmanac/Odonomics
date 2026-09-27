using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads how far a cars.com result card says its car is from the search's zip. An ordinary
/// dealer's card prints it as "<c>&lt;City&gt;, &lt;ST&gt; (N mi)</c>" ("Sanford, FL (28 mi)"), and a CarMax
/// listing carried on cars.com's own search prints it the same way after its delivery fee ("$249 delivery
/// to Orlando, FL (14 mi)"), so one pattern reads both. A card with no such line, whether it shows nothing
/// at all (the site's own nationwide recommendation links padding a later page) or some other text
/// entirely, states no distance.</summary>
public static class CarsComCards
{
    private static readonly Regex DistanceLine = new(@"\((?<mi>\d+)\s*mi\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The distance in miles <paramref name="cardText"/> states, or null when it states none.</summary>
    public static int? ReadDistanceMiles(string cardText)
    {
        Match match = DistanceLine.Match(cardText);
        return match.Success ? int.Parse(match.Groups["mi"].Value) : null;
    }
}
