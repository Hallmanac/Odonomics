using System.Globalization;
using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>Reads the vehicle-history summary CarGurus prints, off a detail page's text or a search card's. A
/// detail page's History block (the heading, then "Clean title", "0 accidents reported", "1 previous owner",
/// and for a car with such a past "Reported as previous rental vehicle" or "Reported as corporate leased
/// vehicle") gives the title wording, the accident and owner counts, and the use statement, and the price area
/// carries "(Frame damage reported)" beside the price. A field the text does not state is null, never zero or
/// clean. Every line pattern is anchored to a whole line, so a dealer's description that says "no accidents" in
/// a sentence states nothing, and the frame-damage parenthetical is only read ahead of the dealer's description
/// for the same reason.</summary>
public static partial class CarGurusHistory
{
    private const string FrameDamage = "Frame damage reported";

    /// <summary>The summary <paramref name="text"/> states, which is <see cref="PostingHistory.None"/> when it
    /// states none of it.</summary>
    public static PostingHistory Read(string text)
    {
        Match accidents = AccidentLine().Match(text);
        Match owners = OwnerLine().Match(text);

        return new PostingHistory(
            ReadTitleWording(text),
            accidents.Success ? Count(accidents) : null,
            owners.Success ? Count(owners) : null,
            ReadUseStatement(text),
            FrameDamageMarker().IsMatch(BeforeDescription(text)) ? FrameDamage : null);
    }

    private static int? Count(Match match) =>
        int.TryParse(match.Groups["n"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            ? count
            : null;

    /// <summary>The first line under a History heading, when it is about the title ("Clean title", "Salvage
    /// title"): the wording as printed. A page can carry the word "History" as a heading more than once, so the
    /// first one whose next line is about the title is the block.</summary>
    private static string? ReadTitleWording(string text)
    {
        foreach (Match heading in HistoryHeading().Matches(text))
        {
            string? firstLine = text[(heading.Index + heading.Length)..]
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0);

            if (firstLine is not null && TitleLine().IsMatch(firstLine))
            {
                return firstLine;
            }
        }

        return null;
    }

    /// <summary>The rental or fleet use the History block states: its "Reported as ..." line when that names such
    /// a use, otherwise the use heading itself ("Work fleet vehicle use"), without a closing period.</summary>
    private static string? ReadUseStatement(string text)
    {
        Match reported = ReportedUseLine().Match(text);
        if (reported.Success)
        {
            return reported.Groups["use"].Value.TrimEnd('.', ' ', '\t');
        }

        Match heading = UseHeadingLine().Match(text);
        return heading.Success
            ? heading.Groups["use"].Value.Trim()
            : null;
    }

    private static string BeforeDescription(string text)
    {
        Match description = DealerDescriptionHeading().Match(text);
        return description.Success
            ? text[..description.Index]
            : text;
    }

    [GeneratedRegex(@"^[ \t]*(?<n>\d+)[ \t]+accidents?[ \t]+reported[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex AccidentLine();

    [GeneratedRegex(@"^[ \t]*(?<n>\d+)[ \t]+previous[ \t]+owners?[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex OwnerLine();

    [GeneratedRegex(@"^[ \t]*History\d*[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HistoryHeading();

    [GeneratedRegex(@"\btitle\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^[ \t]*(?<use>Reported as [^\r\n]*?\b(?:rental|fleet|leased?|corporate|commercial|taxi|police|government|livery)\b[^\r\n]*?)[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ReportedUseLine();

    [GeneratedRegex(@"^[ \t]*(?<use>(?:[\w-]+[ \t]+){0,2}(?:rental|fleet)[ \t]+vehicle[ \t]+use)[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex UseHeadingLine();

    [GeneratedRegex(@"\(\s*Frame damage reported\s*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameDamageMarker();

    [GeneratedRegex(@"^[ \t]*Dealer['’]s description[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DealerDescriptionHeading();
}
