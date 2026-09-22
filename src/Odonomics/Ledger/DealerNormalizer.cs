using System.Text.RegularExpressions;

namespace Odonomics.Ledger;

/// <summary>Normalizes a dealer's name or location for <see cref="DealerEntity"/>'s uniqueness key:
/// collapsed whitespace, trimmed, upper-invariant, so "Holler  Honda " and "holler honda" resolve
/// to the same dealer.</summary>
public static partial class DealerNormalizer
{
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : WhitespaceRun().Replace(value.Trim(), " ").ToUpperInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
