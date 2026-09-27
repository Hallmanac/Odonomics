using System.Globalization;
using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>What a result card says it costs to get the car to the buyer: the one-time shipping fee,
/// and for a car already at a store the city it can be picked up in (see <see cref="CarMaxCards"/>).</summary>
public readonly record struct CardFee(decimal ShippingFee, string? PickupLocation = null);

/// <summary>The model year and mileage a search card states, and whether its own model text says
/// "Hybrid", read off a card such as "View more\nCompare\n2016 Toyota Camry Hybrid\nXLE\n·\n74K mi\n..."
/// (see <see cref="CarMaxCards.ReadVehicleFacets"/>). Either figure is null when the card's text doesn't
/// state it.</summary>
public readonly record struct CardVehicleFacets(int? Year, int? Mileage, bool ModelNamesHybrid);

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

    // The first line of a card that opens a vehicle title: a run of exactly four digits (the model
    // year) followed by more text ("2016 Toyota Camry Hybrid"). CarMax's card carries no condition word
    // or trim on this line (the trim is the line below), unlike a detail page's own title line.
    private static readonly Regex TitleYearLine = new(@"^(?<year>\d{4})[ \t]+\S.*$", RegexOptions.Multiline | RegexOptions.Compiled);

    // A mileage a card rounds to thousands ("40K mi", "74K mi"), the same shorthand CarMax's detail page
    // uses (see ExtractionClient's own thousands-mileage reader), read here as a point estimate for the
    // pre-filter rather than the extraction's own rounding-tolerant ground check.
    private static readonly Regex ThousandsMileageLine = new(@"\b(?<thousands>\d{1,3}(?:\.\d+)?)[ \t]?K[ \t]+mi(?:les)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The model year, mileage, and whether the card's own model text says "Hybrid" (see
    /// <see cref="CardVehicleFacets"/>), read off <paramref name="cardText"/> before its link is ever a
    /// detail-visit candidate. CarMax's search URL carries the scenario's minimum year and maximum
    /// mileage already, but the site does not actually honor the year range (a walk on 2026-09-27 pooled
    /// ten 2014-2017 Camry Hybrid cards from a search whose own URL started at 2018), so this is what
    /// lets the walk drop such a card before spending a detail visit on it (see
    /// <see cref="WalkSite.CollectDetailCards"/>).</summary>
    public static CardVehicleFacets ReadVehicleFacets(string cardText)
    {
        Match titleLine = TitleYearLine.Match(cardText);
        int? year = titleLine.Success && int.TryParse(titleLine.Groups["year"].Value, out int parsedYear) ? parsedYear : null;
        bool namesHybrid = titleLine.Success && titleLine.Value.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

        Match mileageLine = ThousandsMileageLine.Match(cardText);
        int? mileage = mileageLine.Success
            ? (int)Math.Round(
                decimal.Parse(mileageLine.Groups["thousands"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) * 1000m,
                MidpointRounding.AwayFromZero)
            : null;

        return new CardVehicleFacets(year, mileage, namesHybrid);
    }
}
