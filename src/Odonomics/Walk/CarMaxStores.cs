using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the store a CarMax detail page puts its car at, such as "CarMax Orlando". CarMax is
/// many stores under one name, and the store is what the dealer grade and `odo show` have to tell
/// apart, so it is read here and not left to the extraction: the prompt asks for the selling dealer and
/// not the listing site's name, and a model given a page full of "CarMax" reads that as the site's name
/// and returns none. The page's header states the store on a line of its own, either "Test drive at
/// CarMax Orlando, FL" for a car at a nearby store or "Ships from CarMax Clearwater, FL" for one that
/// has to be moved. The stores of the similar cars listed further down are never read, since only the
/// first such line counts.</summary>
public static class CarMaxStores
{
    private const string ChainName = "CarMax";

    private static readonly Regex StoreLine = new(
        @"^[ \t]*(?:Test drive at|Ships from|Available at)[ \t]+CarMax[ \t]+(?<city>[A-Z][A-Za-z.'’-]*(?:[ \t]+[A-Za-z][A-Za-z.'’-]*)*?)(?:,[ \t]*(?<state>[A-Z]{2}))?[ \t]*$",
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
}
