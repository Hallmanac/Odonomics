using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the one-time shipping fee off a carvana detail page's text. The page prints it
/// beside the price ("$1,290 shipping" or "Free shipping", then "Get it Saturday"), and again in
/// its delivery block ("$1,290 Shipping"). It is read here and not by the extraction model because
/// the figure is a plain line of text that decides what a car costs to take home, so it should
/// never depend on a model's reading.</summary>
public static class CarvanaShipping
{
    /// <summary>The page's own "Need it sooner?" block lists other cars, and a card there can carry
    /// that car's shipping line; the page's own line always comes before the block.</summary>
    private const string SimilarVehiclesMarker = "Need it sooner?";

    private static readonly Regex ShippingLine = new(
        @"^\s*(?:free\s+shipping|\$(?<fee>\d[\d,]*(?:\.\d{2})?)\s+shipping)\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The fee the page shows: 0 for "Free shipping", the dollar amount for "$1,590
    /// shipping", and null when the page shows neither, so a fee that was never printed is never
    /// mistaken for free.</summary>
    public static decimal? Read(string pageText)
    {
        int similarVehicles = pageText.IndexOf(SimilarVehiclesMarker, StringComparison.OrdinalIgnoreCase);
        string ownPageText = similarVehicles >= 0 ? pageText[..similarVehicles] : pageText;

        Match line = ShippingLine.Match(ownPageText);
        return line switch
        {
            { Success: false } => null,
            { Groups: [_, { Success: true } fee] } => decimal.Parse(fee.Value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
            _ => 0m,
        };
    }

    /// <summary>The same fee <see cref="Read"/> reads off a detail page, read instead off a search card's own
    /// text ("Free shipping" or "$690 shipping" sits on its own line on a card exactly as it does on the
    /// page), wrapped as a <see cref="CardFee"/> so <see cref="WalkSite.CardFeeReader"/> can read it the same
    /// way CarMax's own card fee is read, and the scenario's price ceiling counts a Carvana card's stated
    /// fee the same way it counts CarMax's.</summary>
    public static CardFee? ReadCardFee(string cardText) => Read(cardText) is decimal fee ? new CardFee(fee) : null;
}
