using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the delivery lines of a CarGurus search card. A card for a car away from the buyer prints
/// how it gets there and what that costs, and the amount is inside the asking price the card shows:
/// "Price includes $462 shipping" under "Home delivery from Delray Beach, FL" or "Store transfer to
/// Orlando, FL", or "Free home delivery". A car at a nearby dealer prints neither. The card's price is
/// the one the walk stores (see <see cref="WalkSite.AskingPriceFromCard"/>) because of that: a store-transfer
/// detail page also lists the dealer's price at its lot, without the shipping.</summary>
public static class CarGurusCards
{
    private static readonly Regex ShippingLine = new(
        @"^[ \t]*Price includes[ \t]+\$[ \t]*(?<fee>\d[\d,]*(?:\.\d{2})?)[ \t]+shipping[ \t\r]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex FreeDeliveryLine = new(
        @"^[ \t]*Free home delivery[ \t\r]*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>A card that states plainly it has no price ("No Price Listed", recorded card for
    /// JTDADABU0T3033887: "Home delivery from Downers Grove, IL / Price includes $1,498 shipping / No
    /// Rating / No Price Listed / Check availability"), even one that also names a shipping amount of
    /// its own: that amount is what the delivery costs, not what the car costs, and
    /// <see cref="IncludedShipping"/> already never reads it as the price, but the card is still no
    /// candidate for a detail visit, so <see cref="WalkSite.NoPriceCardPattern"/> drops it before one
    /// is ever spent finding that out the slow way.</summary>
    public static readonly Regex NoPriceListed = new(
        @"^[ \t]*No Price Listed[ \t\r]*$",
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
