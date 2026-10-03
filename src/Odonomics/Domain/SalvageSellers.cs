using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>
/// The one list of salvage and repairable-vehicle outlets a VIN-history dealer name is checked
/// against (see <see cref="RedFlagsEvaluator.Evaluate"/>'s <c>salvage-seller</c> flag). A car that
/// passed through one of these marketplaces was almost certainly an insurance total loss, which
/// points to a salvage or rebuilt title. Matching is case-insensitive and word-for-word on the
/// dealer name with punctuation ignored, so "Erepairables.com" and "COPART INC" match while a dealer
/// whose name merely contains the letters of a short outlet name does not. Each spelling variant
/// listed here is one entry, since the data spells some outlets both joined and spaced.
/// </summary>
public static partial class SalvageSellers
{
    public static readonly IReadOnlyList<string> Outlets =
    [
        "Erepairables",
        "Copart",
        "IAA",
        "Insurance Auto Auctions",
        "SalvageBid",
        "Salvage Bid",
        "Salvage Reseller",
        "SalvageReseller",
        "A Better Bid",
        "ABetterBid",
        "AutoBidMaster",
        "Auto Bid Master",
    ];

    private static readonly IReadOnlyList<string[]> OutletWords = [.. Outlets.Select(Words)];

    /// <summary>True when <paramref name="dealerName"/> names one of <see cref="Outlets"/>, meaning
    /// its words contain an outlet's words as a contiguous run.</summary>
    public static bool IsSalvageSeller(string? dealerName)
    {
        if (string.IsNullOrWhiteSpace(dealerName))
        {
            return false;
        }

        string[] words = Words(dealerName);
        return OutletWords.Any(outlet => ContainsRun(words, outlet));
    }

    private static bool ContainsRun(string[] words, string[] run)
    {
        for (int start = 0; start + run.Length <= words.Length; start++)
        {
            if (words.AsSpan(start, run.Length).SequenceEqual(run))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] Words(string value)
    {
        string spaced = NonWord().Replace(value, " ").Trim().ToLowerInvariant();
        return spaced.Length == 0
            ? []
            : [.. spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries)];
    }

    [GeneratedRegex(@"[^\w]+")]
    private static partial Regex NonWord();
}
