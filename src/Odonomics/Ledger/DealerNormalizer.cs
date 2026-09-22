using System.Text.RegularExpressions;

namespace Odonomics.Ledger;

/// <summary>Normalizes a dealer's name or location for <see cref="DealerEntity"/>'s uniqueness key:
/// punctuation dropped, whitespace collapsed, trimmed, upper-invariant, so "Holler  Honda ",
/// "holler honda", and "Winter Park, FL" vs "Winter Park FL" resolve to the same dealer.</summary>
public static partial class DealerNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        string withoutPunctuation = Punctuation().Replace(value, " ");
        return WhitespaceRun().Replace(withoutPunctuation, " ").Trim().ToUpperInvariant();
    }

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
