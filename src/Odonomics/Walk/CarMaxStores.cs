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

    private const string OnlyAtPrefix = "Only at ";

    /// <summary>The store name after "Only at " in <paramref name="pickupLocation"/> (a CarMax
    /// posting's own <see cref="Ledger.PostingEntity.PickupLocation"/>), or null when it isn't in
    /// that shape or names none. CarMax has no pickup reader of its own (see
    /// <see cref="WalkSites.CarMax"/>), so this field is always read off a search card's own text
    /// (<see cref="CarMaxCards.ReadFee"/>'s "Available today" line), and for a car CarMax will not
    /// transfer that line reads "Available today·Only at Norco", which lands here verbatim as
    /// "Only at Norco" rather than a bare city: CarMax prints no delivery option for a car like
    /// this at all, only its one store, so this is the one place that fact survives into the
    /// ledger.</summary>
    public static string? OnlyAtStoreName(string? pickupLocation) =>
        pickupLocation is not null && pickupLocation.StartsWith(OnlyAtPrefix, StringComparison.Ordinal)
            ? pickupLocation[OnlyAtPrefix.Length..].Trim()
            : null;

    /// <summary>Known CarMax store locations, keyed by the short name <see cref="OnlyAtStoreName"/>
    /// reads off a card ("Norco", "San Gabriel Valley/Duarte"): latitude and longitude in decimal
    /// degrees, close enough to the store's own street address to judge whether it sits inside or
    /// outside a scenario's radius (see <see cref="DistanceMilesFromZip"/>). Curated by hand from
    /// CarMax's public store locator as new "Only at" stores turn up in a walk's own postings; a
    /// store not yet in here is left unmeasured rather than guessed at.</summary>
    private static readonly IReadOnlyDictionary<string, (double Latitude, double Longitude)> StoreLocations = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase)
    {
        ["Norco"] = (33.9312, -117.5578),
        ["Irvine"] = (33.6822, -117.7423),
        ["Buena Park"] = (33.8642, -117.9987),
        ["LAX"] = (33.9425, -118.4081),
        ["Modesto"] = (37.6650, -120.9958),
        ["San Gabriel Valley/Duarte"] = (34.1400, -117.9773),
        ["Laurel"] = (39.1057, -76.8477),
        ["White Marsh"] = (39.3899, -76.4327),
        ["Orlando"] = (28.4728, -81.4331),
        ["Doral"] = (25.8195, -80.3553),
        ["Ft. Lauderdale"] = (26.0744, -80.2569),
        ["Daytona"] = (29.1400, -81.0600),
        ["Clearwater"] = (28.0064, -82.7395),
    };

    /// <summary>The centroid of a scenario zip this file knows the location of, for
    /// <see cref="DistanceMilesFromZip"/>: every zip a shipped or tested scenario actually uses
    /// (see scenarios/daughter.json and the test scenarios), curated the same way as
    /// <see cref="StoreLocations"/>. A zip not yet in here is left unmeasured, same as an unknown
    /// store.</summary>
    private static readonly IReadOnlyDictionary<string, (double Latitude, double Longitude)> ZipLocations = new Dictionary<string, (double, double)>
    {
        ["32833"] = (28.5551, -81.0864),
        ["32114"] = (29.2108, -81.0228),
    };

    /// <summary>The great-circle distance in miles between <paramref name="storeName"/> (the name
    /// <see cref="OnlyAtStoreName"/> reads) and <paramref name="zip"/>, or null when either isn't in
    /// this file's own curated location tables: a distance nobody actually measured never proves a
    /// store in or out of a scenario's radius.</summary>
    public static double? DistanceMilesFromZip(string storeName, string zip)
    {
        if (!StoreLocations.TryGetValue(storeName, out (double Latitude, double Longitude) store)
            || !ZipLocations.TryGetValue(zip, out (double Latitude, double Longitude) origin))
        {
            return null;
        }

        return HaversineMiles(store, origin);
    }

    private const double EarthRadiusMiles = 3958.8;

    private static double HaversineMiles((double Latitude, double Longitude) a, (double Latitude, double Longitude) b)
    {
        double lat1 = DegreesToRadians(a.Latitude);
        double lat2 = DegreesToRadians(b.Latitude);
        double deltaLat = DegreesToRadians(b.Latitude - a.Latitude);
        double deltaLon = DegreesToRadians(b.Longitude - a.Longitude);
        double h = (Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2));
        return EarthRadiusMiles * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

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
