using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>The pickup option a detail page offers beside delivery: where the car can be collected
/// and what that costs. <paramref name="Fee"/> is 0 when the page prints no fee under the pickup
/// option, which is what carvana does today.</summary>
public sealed record PickupOption(string Location, decimal Fee);

/// <summary>Reads the pickup option off a carvana detail page's "Pickup and Delivery" block. The block
/// lists two options, "Pick it up from our Orlando location" then "Orlando, FL", then "or", then
/// "Delivery Tuesday" with the shipping fee under it. Like <see cref="CarvanaShipping"/>, it is read
/// here and not by the extraction model, since the figure decides what a car costs to take home.
/// The block renders lazily, only once the page has been scrolled to it, so a page that lacks it
/// reads as no pickup option rather than as a free one.</summary>
public static class CarvanaPickup
{
    /// <summary>The line that opens the block; the walk scrolls a carvana detail page until the page
    /// text carries it (see <see cref="WalkSite.LazyDetailBlockMarker"/>).</summary>
    public const string BlockHeading = "Pickup and Delivery";

    private static readonly Regex PickupLine = new(
        @"^\s*Pick it up from our (?<place>.+?) location\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LocationLine = new(
        @"^\s*(?<location>[A-Z][A-Za-z.'\- ]*, [A-Z]{2})\s*$",
        RegexOptions.Compiled);

    private static readonly Regex FeeLine = new(
        @"^\s*(?:(?<free>free)\b.*|\$(?<fee>\d[\d,]*(?:\.\d{2})?)(?:\s+\w+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Where a block's pickup option ends: the "or" between the two options, the delivery
    /// option, or whatever section follows the block.</summary>
    private static readonly Regex OptionEnd = new(
        @"^\s*(?:or|Delivery\b.*|More to Love|Need it sooner\?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The pickup option the page's own block shows, or null when the page has no such block
    /// or its block offers no pickup. The location is the city line under the pickup line ("Orlando,
    /// FL"), falling back to the name in the pickup line itself; the fee is a dollar amount or "Free"
    /// printed under the pickup option, and 0 when none is printed.</summary>
    public static PickupOption? Read(string pageText)
    {
        string[] lines = pageText.Split('\n');
        int heading = Array.FindIndex(lines, l => l.Trim().Equals(BlockHeading, StringComparison.OrdinalIgnoreCase));
        if (heading < 0)
        {
            return null;
        }

        string[] option = [.. lines.Skip(heading + 1).TakeWhile(l => !OptionEnd.IsMatch(l))];
        int pickup = Array.FindIndex(option, l => PickupLine.IsMatch(l));
        if (pickup < 0)
        {
            return null;
        }

        string[] afterPickup = option[(pickup + 1)..];
        string location = afterPickup
            .Select(l => LocationLine.Match(l))
            .FirstOrDefault(m => m.Success)?.Groups["location"].Value
            ?? PickupLine.Match(option[pickup]).Groups["place"].Value;

        Match feeLine = afterPickup.Select(l => FeeLine.Match(l)).FirstOrDefault(m => m.Success) ?? Match.Empty;
        decimal fee = feeLine.Groups["fee"] is { Success: true } amount
            ? decimal.Parse(amount.Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : 0m;

        return new PickupOption(location, fee);
    }
}
