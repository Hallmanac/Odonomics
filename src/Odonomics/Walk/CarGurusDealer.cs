using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>Reads the dealer a CarGurus detail page names, ahead of the extraction: CarGurus has no
/// fallback dealer (unlike carvana), so a page the extraction misreads stores nothing rather than a
/// wrong hub, and the page's own text already states both the name and the location plainly enough
/// to read deterministically.
///
/// <para>The name is the first line under the page's own "Dealer" heading that is not one of that
/// heading's own badge lines ("TOP DIGITAL" or "TOP  RATED", each followed by a lone "DEALER" line
/// and a lone four-digit year, recorded detail JTDBCMFE4R3040782: "Dealer / TOP DIGITAL / DEALER /
/// 2026 / Action Auto Sales and Finance Inc."). A page whose "Dealer" heading is followed directly by
/// "Dealer reviews" or "Dealer's description" names no dealer at all (recorded detail
/// JTDBCMFE8S3078635), and the name reads as null rather than either of those section labels.</para>
///
/// <para>The location is never read off the dealer's own street address, whose shape varies too much
/// to parse reliably; two lines the page reads the same way on every recording found so far cover
/// every case a name is worth reading for. A delivered car names its true origin only in its "Choose
/// pickup or delivery" step ("Price includes $2,887 to Orlando, FL. To buy without the delivery fee,
/// choose pick up in Lehi, UT.", recorded detail JTDBCMFE4R3040782 line 153): the address a block
/// under "Dealer" also gives can agree with it, but the pickup line is the one line CarGurus prints
/// for exactly this purpose, so it is read first. A car at or shipped from a dealer prints its city
/// instead on a line of its own or beside its mileage ("Mileage: 30,992 · Clermont, FL (38 mi away)",
/// recorded detail JTDBCMFE8S3078635, which names no dealer at all beyond that city), read only when
/// no pickup line is present, since a delivered car's own mileage line instead names the buyer's
/// destination, not the dealer's.</para></summary>
public static class CarGurusDealer
{
    private const string DealerHeading = "Dealer";

    // "Top Rated Dealer" and "Top Digital Dealer" print as three lines of their own ahead of the
    // name ("TOP  RATED" or "TOP DIGITAL", then "DEALER", then a lone four-digit year): matched as
    // that whole three-line sequence, not line by line, so a dealer whose real name happens to start
    // with "Top" (a "Top Line Motors", say) is never mistaken for this badge and skipped along with
    // whatever line happens to follow it.
    private static readonly Regex TopBadgeFirstLine = new(@"^TOP[ \t ]+\S.*$", RegexOptions.Compiled);
    private static readonly Regex FourDigitYear = new(@"^\d{4}$", RegexOptions.Compiled);

    private static readonly Regex PickupLine = new(
        @"choose pick up in[ \t]+(?<city>[A-Za-z][A-Za-z .'-]*?)[ \t]*,[ \t]*(?<state>[A-Z]{2})\.",
        RegexOptions.Compiled);

    private static readonly Regex MileageAwayLine = new(
        @"^(?:[ \t]*Mileage:[^\n]*?·[ \t]*)?(?<city>[A-Za-z][A-Za-z .'-]*?)[ \t]*,[ \t]*(?<state>[A-Z]{2})[ \t]*\([ \t]*\d+[ \t]*mi away\)[ \t\r]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>The dealer this page's own text names: a name when its "Dealer" block gives one, a
    /// location when either its pickup line or its mileage-away line gives one, both, or neither (in
    /// which case this returns null and <see cref="WalkSite.ResolveDealer"/> falls back to whatever
    /// the extraction made of the page).</summary>
    public static ResolvedDealer? Read(string pageText)
    {
        string? name = ReadName(pageText);
        string? location = ReadLocation(pageText);
        return name is null && location is null ? null : new ResolvedDealer(name, location, IsFallback: false);
    }

    private static string? ReadName(string pageText)
    {
        string[] lines = pageText.Split('\n');
        int dealerAt = Array.FindIndex(lines, l => l.Trim() == DealerHeading);
        if (dealerAt < 0)
        {
            return null;
        }

        int i = dealerAt + 1;
        while (i < lines.Length && lines[i].Trim().Length == 0)
        {
            i++;
        }

        if (i + 2 < lines.Length
            && TopBadgeFirstLine.IsMatch(lines[i].Trim())
            && lines[i + 1].Trim() == "DEALER"
            && FourDigitYear.IsMatch(lines[i + 2].Trim()))
        {
            i += 3;
            while (i < lines.Length && lines[i].Trim().Length == 0)
            {
                i++;
            }
        }

        if (i >= lines.Length)
        {
            return null;
        }

        string line = lines[i].Trim();
        return line is "Dealer reviews" or "Dealer's description" ? null : line;
    }

    private static string? ReadLocation(string pageText)
    {
        Match pickup = PickupLine.Match(pageText);
        if (pickup.Success)
        {
            return Location(pickup);
        }

        Match mileageAway = MileageAwayLine.Match(pageText);
        return mileageAway.Success ? Location(mileageAway) : null;
    }

    private static string Location(Match match) => $"{match.Groups["city"].Value.Trim()}, {match.Groups["state"].Value}";
}
