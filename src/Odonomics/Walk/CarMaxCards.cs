using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>What a result card says it costs to get the car to the buyer: the one-time shipping fee,
/// and for a car already at a store the city it can be picked up in (see <see cref="CarMaxCards"/>).</summary>
public readonly record struct CardFee(decimal ShippingFee, string? PickupLocation = null);

/// <summary>Reads the availability line of a CarMax search card. CarMax prints what taking the car
/// home costs on the card itself: "Available today·Orlando" for a car in stock at a nearby store, and
/// "$49 shipping·Get it by Monday" or "$149 shipping·Get it by Sep 28 - Oct 2" for a car transferred
/// from another store. The card is where the fee is read, and not the detail page, because the fee
/// is per car and the search already shows it; the detail page's own "optional shipping" line is
/// followed by a list of similar cars with their own fees, which a reader would have to steer around.</summary>
public static class CarMaxCards
{
    private static readonly Regex ShippingLine = new(
        @"\$\s*(?<fee>\d[\d,]*(?:\.\d{2})?)\s+shipping\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The separator is a middle dot; a bullet is accepted too since the two look alike on a page.
    private static readonly Regex AvailableTodayLine = new(
        @"Available\s+today\s*[·•]\s*(?<city>[^\r\n·•|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The fee the card shows: the dollar amount for "$149 shipping", and 0 with the city as
    /// the pickup location for "Available today·Orlando". A card that shows neither reads as null, so a
    /// fee that was never printed is never mistaken for free.</summary>
    public static CardFee? ReadFee(string cardText)
    {
        Match shipping = ShippingLine.Match(cardText);
        if (shipping.Success)
        {
            return new CardFee(decimal.Parse(shipping.Groups["fee"].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        }

        Match availableToday = AvailableTodayLine.Match(cardText);
        return availableToday.Success && availableToday.Groups["city"].Value.Trim() is { Length: > 0 } city
            ? new CardFee(0m, city)
            : null;
    }
}
