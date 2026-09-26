using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>Reads the badges a site prints on a result card, for the sites whose card shape has been
/// confirmed from a recorded search page. Each reader takes the card's visible text and returns the
/// display-only facts it finds, keyed by <see cref="PostingAttributeNames"/>; a card with no badge
/// returns nothing, so nothing is recorded for it. A badge is a line of the card that is exactly the
/// badge's text, capitalized as the recorded cards print it, which keeps a sentence that merely
/// mentions one ("Shop All Price Drops") and a filter ("Free Shipping") from reading as one. The values are the badge's own wording, so `odo show` prints what the site said.
/// These are the sites' opinions of a price and never an input to the cost model or the ranking.</summary>
public static class CardBadges
{
    /// <summary>A dealer rating on cars.com's card: a line of its own, one digit, a point, one digit.</summary>
    private static readonly Regex DealerRatingLine = new(@"^[1-5]\.\d$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> NoBadges = new Dictionary<string, string>();

    /// <summary>cars.com prints a deal badge (Great, Good, or Fair Deal), a "High Demand" badge, and the
    /// dealer's rating a couple of lines after the title.</summary>
    public static IReadOnlyDictionary<string, string> CarsCom(string cardText) => Read(
        cardText,
        [
            ("Great Deal", PostingAttributeNames.Deal),
            ("Good Deal", PostingAttributeNames.Deal),
            ("Fair Deal", PostingAttributeNames.Deal),
            ("High Demand", PostingAttributeNames.Demand),
        ],
        DealerRatingLine);

    /// <summary>autotrader prints a price badge (Great Price or Good Price), "Price Drop", and
    /// "Online Paperwork". It also prints "High Demand" and others that are not recorded.</summary>
    public static IReadOnlyDictionary<string, string> Autotrader(string cardText) => Read(
        cardText,
        [
            ("Great Price", PostingAttributeNames.Deal),
            ("Good Price", PostingAttributeNames.Deal),
            ("Price Drop", PostingAttributeNames.PriceDrop),
            ("Online Paperwork", PostingAttributeNames.Paperwork),
        ]);

    /// <summary>carvana prints "Great Deal", "Price Drop", and "Free shipping". Its "Recent" tag is a sort
    /// label the page repeats on cards, not a badge, so it is not read.</summary>
    public static IReadOnlyDictionary<string, string> Carvana(string cardText) => Read(
        cardText,
        [
            ("Great Deal", PostingAttributeNames.Deal),
            ("Price Drop", PostingAttributeNames.PriceDrop),
            ("Free shipping", PostingAttributeNames.Shipping),
        ]);

    /// <summary>CarGurus prints its verdict on the price as a line of its own: "Great Deal", "Good Deal", "Fair Deal",
    /// "High Priced", or "Overpriced", and "Uncertain" for a car it could not rate, which says nothing and is not read.</summary>
    public static IReadOnlyDictionary<string, string> CarGurus(string cardText) => Read(
        cardText,
        [
            ("Great Deal", PostingAttributeNames.Deal),
            ("Good Deal", PostingAttributeNames.Deal),
            ("Fair Deal", PostingAttributeNames.Deal),
            ("High Priced", PostingAttributeNames.Deal),
            ("Overpriced", PostingAttributeNames.Deal),
        ]);

    /// <summary>Every card line that is one of <paramref name="badges"/>, or that matches
    /// <paramref name="dealerRatingLine"/> when one is given, in card order. A name a card shows twice
    /// keeps its first value.</summary>
    private static IReadOnlyDictionary<string, string> Read(string cardText, (string Text, string Name)[] badges, Regex? dealerRatingLine = null)
    {
        Dictionary<string, string> found = [];
        foreach (string line in cardText.Split('\n').Select(l => l.Trim()))
        {
            if (dealerRatingLine is not null && dealerRatingLine.IsMatch(line))
            {
                found.TryAdd(PostingAttributeNames.DealerRating, line);
                continue;
            }

            foreach ((string text, string name) in badges)
            {
                if (string.Equals(line, text, StringComparison.Ordinal))
                {
                    found.TryAdd(name, text);
                }
            }
        }

        return found.Count == 0 ? NoBadges : found;
    }
}
