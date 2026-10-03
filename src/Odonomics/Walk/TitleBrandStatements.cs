using System.Text.RegularExpressions;

namespace Odonomics.Walk;

/// <summary>What a listing's own detail text says about a title brand: the first title-status phrase
/// it states, such as "rebuilt title" or "Title status: Salvage", exactly as the page printed it. The
/// phrase is a claim by the listing, so a page that says nothing, or says "clean title", yields null,
/// and a null is never a claim that the title is clean.</summary>
public static partial class TitleBrandStatements
{
    /// <summary>The first title-brand phrase <paramref name="pageText"/> states, as read, or null when
    /// it states none. Only a brand word tied to the word title counts, so a page that merely mentions
    /// a flood zone, a lemon-colored trim, or "clean title" raises nothing. Three shapes are matched, all
    /// case-insensitively: the brand before the word ("Rebuilt title", "salvaged title"), a title
    /// label followed by the brand on the same line ("Title status: Salvage"), and a title heading
    /// whose value is on a later line of its own, the way a cars.com history block prints its clean
    /// state ("Title", a blank line, "Clean"). A phrase read from the last shape is kept with its
    /// line breaks collapsed to single spaces ("Title Salvage").
    /// A phrase in a sentence that negates it ("No salvage, flood or rebuilt title") is skipped,
    /// because dealers print exactly that as a selling point.</summary>
    public static string? Read(string pageText)
    {
        foreach (Match match in TitleBrand().Matches(pageText))
        {
            if (!IsNegated(pageText, match.Index))
            {
                return Regex.Replace(match.Value.Trim(), @"\s+", " ");
            }
        }

        return null;
    }

    /// <summary>Whether the sentence holding the match negates it: a "no", "not", "never", "without",
    /// "non", "free of", or a "n't" contraction ahead of the match in the same sentence. The sentence
    /// starts after the last full stop, semicolon, or line break before the match, so a negation in an
    /// earlier line never hides a later statement.</summary>
    private static bool IsNegated(string pageText, int matchIndex)
    {
        int sentenceStart = pageText.LastIndexOfAny(['.', ';', '!', '?', '\n', '\r'], Math.Max(matchIndex - 1, 0)) + 1;
        if (matchIndex <= sentenceStart)
        {
            return false;
        }

        return Negation().IsMatch(pageText.AsSpan(sentenceStart, matchIndex - sentenceStart));
    }

    private const string Brand = @"(?:rebuilt|reconstructed|salvage[d]?|branded|flood(?:ed)?|lemon(?:[ -]law)?|buy[- ]?back)";

    [GeneratedRegex($@"(?<![\w-]){Brand}[ \t]+title\b|(?<![\w-])title(?:[ \t]+(?:status|brand|type))?[ \t]*:[ \t]*{Brand}\b|^[ \t]*title[ \t]*\r?\n(?:[ \t]*\r?\n)*[ \t]*{Brand}[ \t\r]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex TitleBrand();

    [GeneratedRegex(@"\b(?:no|not|never|without|non|free of)\b|n't\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Negation();
}
