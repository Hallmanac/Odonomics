using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the delivery lines of a CarGurus search card. A card for a car away from the buyer prints
/// how it gets there and what that costs, and the amount is inside the asking price the card shows:
/// "Price includes $462 shipping" under "Home delivery from Delray Beach, FL" or "Store transfer to
/// Orlando, FL", or "Free home delivery". A car at a nearby dealer prints neither. The card's price is
/// the one the walk stores (see <see cref="WalkSite.AskingPriceFromCard"/>) because of that: the detail page
/// shows the car's price at its lot, without the shipping.</summary>
public static class CarGurusCards
{
    private static readonly Regex ShippingLine = new(
        @"^[ \t]*Price includes[ \t]+\$[ \t]*(?<fee>\d[\d,]*(?:\.\d{2})?)[ \t]+shipping[ \t\r]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex FreeDeliveryLine = new(
        @"^[ \t]*Free home delivery[ \t\r]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The shipping amount a "Price includes $462 shipping" line names, or null when the card has no such line.</summary>
    public static decimal? IncludedShipping(string cardText)
    {
        Match shipping = ShippingLine.Match(cardText);
        return shipping.Success
            ? decimal.Parse(shipping.Groups["fee"].Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>The shipping fee the card shows: the amount of its "Price includes $462 shipping" line, and 0 for
    /// "Free home delivery". A card that shows neither reads as null, so a fee that was never printed is never mistaken for free.</summary>
    public static CardFee? ReadFee(string cardText) =>
        IncludedShipping(cardText) is decimal shipping
            ? new CardFee(shipping)
            : FreeDeliveryLine.IsMatch(cardText)
                ? new CardFee(0m)
                : null;
}
