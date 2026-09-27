using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the store a CarMax detail page puts its car at, such as "CarMax Orlando". CarMax is
/// many stores under one name, and the store is what the dealer grade and `odo show` have to tell
/// apart, so it is read here and not left to the extraction: the prompt asks for the selling dealer and
/// not the listing site's name, and a model given a page full of "CarMax" reads that as the site's name
/// and returns none. The page's header states the store on a line of its own: "Test drive at CarMax
/// Orlando, FL" for a car at a nearby store, "Ships from CarMax Clearwater, FL" for one that has to be
/// moved, "Available at CarMax Daytona" or "Only at CarMax Laurel, MD" for one already on a lot,
/// "Reserved at CarMax North Houston, TX" for one held for another buyer, or "Coming to CarMax Mobile,
/// AL" for one still in transit (see <see cref="ReadAvailability"/> for those last two). A store's own
/// name can carry a slash between two cities that share one store ("Arlington/Ft. Worth") or a
/// parenthetical ("Jackson (MS)", "Boise (Meridian)"). The stores of the similar cars listed further
/// down are never read, since only the first such line counts.</summary>
public static class CarMaxStores
{
    private const string ChainName = "CarMax";

    /// <summary>What <see cref="ReadAvailability"/> returns for a page whose header read "Reserved
    /// at": the car is on hold for another buyer, not purchasable by anyone else.</summary>
    public const string Reserved = "Reserved for another buyer";

    /// <summary>What <see cref="ReadAvailability"/> returns for a page whose header read "Coming
    /// to": the car has not arrived at the store yet, so it cannot be bought until it does.</summary>
    public const string ComingSoon = "In transit, not yet purchasable";

    private static readonly Regex StoreLine = new(
        @"^[ \t]*(?<prefix>Test drive at|Ships from|Available at|Only at|Reserved at|Coming to)[ \t]+CarMax[ \t]+(?<city>[A-Z][A-Za-z.'’()/-]*(?:[ \t]+[A-Za-z(][A-Za-z.'’()/-]*)*?)(?:,[ \t]*(?<state>[A-Z]{2}))?[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The store the page's first store line names, as the dealer "CarMax &lt;city&gt;" located
    /// at the city and state the line prints (the city alone when it prints no state), or null when the
    /// page names no store.</summary>
    public static ResolvedDealer? Read(string pageText)
    {
        Match match = StoreLine.Match(pageText);
        if (!match.Success)
        {
            return null;
        }

        string city = Regex.Replace(match.Groups["city"].Value, @"\s+", " ");
        string location = match.Groups["state"].Success
            ? $"{city}, {match.Groups["state"].Value}"
            : city;
        return new ResolvedDealer($"{ChainName} {city}", location, IsFallback: false);
    }

    /// <summary>What the page's first store line says about whether the car can actually be bought
    /// right now: <see cref="Reserved"/> for a "Reserved at" header, <see cref="ComingSoon"/> for a
    /// "Coming to" header, or null for every other header (including a page naming no store at all),
    /// since a car at a store or shipping from one is ordinarily purchasable.</summary>
    public static string? ReadAvailability(string pageText)
    {
        Match match = StoreLine.Match(pageText);
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["prefix"].Value switch
        {
            "Reserved at" => Reserved,
            "Coming to" => ComingSoon,
            _ => null,
        };
    }
}
