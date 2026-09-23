using System.Text.RegularExpressions;
using Odonomics.CarEdge;

namespace Odonomics.Ledger;

/// <summary>Normalizes a dealer's name or location for <see cref="DealerEntity"/>'s uniqueness key:
/// punctuation dropped, whitespace collapsed, trimmed, upper-invariant, so "Holler  Honda ",
/// "holler honda", and "Winter Park, FL" vs "Winter Park FL" resolve to the same dealer. A location is
/// keyed by <see cref="NormalizeLocation"/>, which also expands "Ft.", "Mt." and "St." in the city, so
/// "St. Augustine, FL" and "Saint Augustine, FL" do too.</summary>
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

    /// <summary>Normalizes a dealer's location for the uniqueness key: <see cref="Normalize"/>'s
    /// output for the city followed by the state, with the city's "Ft.", "Mt." and "St." expanded to
    /// Fort, Mount and Saint and any trailing zip code dropped, the same reading
    /// <see cref="DealerLocationParts"/> gives a location. A location with no such abbreviation or
    /// zip keys exactly as <see cref="Normalize"/> would.</summary>
    public static string NormalizeLocation(string? location)
    {
        DealerLocationParts parts = DealerLocationParts.Parse(location);
        return $"{parts.City} {parts.State}".Trim();
    }

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
