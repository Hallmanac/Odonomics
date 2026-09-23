using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.CarEdge;

/// <summary>A dealer or card location split into its city and state after normalization, so two
/// locations can be compared part by part instead of as raw text. Either part can be empty: an API
/// source can report only a city or only a state, and a bare part is read as a state only when it is
/// a two-letter code, since a card's own state is always one. The normalization case-folds, drops
/// punctuation and any trailing zip code, and expands "Ft.", "Mt." and "St." anywhere in a city
/// to Fort, Mount and Saint (a bare "MT" location is Montana, so the expansion never touches the
/// state part).</summary>
public readonly partial record struct DealerLocationParts(string City, string State)
{
    public bool IsEmpty => City.Length == 0 && State.Length == 0;

    /// <summary>True when exactly one of the two parts is known, which is when a same-named card
    /// elsewhere in that city or state cannot be told apart from the dealer's own.</summary>
    public bool IsPartial => (City.Length == 0) != (State.Length == 0);

    public static DealerLocationParts Parse(string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return new DealerLocationParts("", "");
        }

        string withoutZip = TrailingZip().Replace(location, "");
        int comma = withoutZip.LastIndexOf(',');
        if (comma >= 0)
        {
            return Build(withoutZip[..comma], withoutZip[(comma + 1)..]);
        }

        string normalized = DealerNormalizer.Normalize(withoutZip);
        int lastSpace = normalized.LastIndexOf(' ');
        string lastWord = normalized[(lastSpace + 1)..];
        return IsStateCode(lastWord)
            ? Build(lastSpace < 0 ? "" : normalized[..lastSpace], lastWord)
            : Build(normalized, "");
    }

    /// <summary>A card matches when every part the dealer has equals the card's same part; a part
    /// the dealer lacks is not compared.</summary>
    public bool Matches(DealerLocationParts card) =>
        (City.Length == 0 || City == card.City) && (State.Length == 0 || State == card.State);

    private static DealerLocationParts Build(string rawCity, string rawState)
    {
        string[] cityWords = [.. DealerNormalizer.Normalize(rawCity)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word switch
            {
                "FT" => "FORT",
                "MT" => "MOUNT",
                "ST" => "SAINT",
                _ => word,
            })];
        return new DealerLocationParts(string.Join(' ', cityWords), DealerNormalizer.Normalize(rawState));
    }

    private static bool IsStateCode(string word) => word.Length == 2 && word.All(char.IsLetter);

    [GeneratedRegex(@"[\s,]*\b\d{5}(?:-\d{4})?\s*$")]
    private static partial Regex TrailingZip();
}
