using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>
/// The one list of salvage and repairable-vehicle outlets a VIN-history dealer name is checked
/// against (see <see cref="RedFlagsEvaluator.Evaluate"/>'s <c>salvage-seller</c> flag). A VIN history
/// row from one of these marketplaces is a damaged-vehicle marketplace sighting, not a confirmed
/// salvage title: the car may have been an insurance total loss, but a self-insured fleet, an insurer
/// that does not report to NICB, uninsured damage, or a mechanical fault would also put it there. A
/// clean NICB VINCheck therefore does not clear it, because not every insurer reports to NICB.
/// Matching is case-insensitive and word-for-word on the
/// dealer name with punctuation ignored, so "Erepairables.com" and "COPART INC" match while a dealer
/// whose name merely contains the letters of a short outlet name does not. Each spelling variant
/// listed here is one entry, since the data spells some outlets both joined and spaced. A dealer name
/// that contains the whole word "salvage", "repairable", or "repairables" matches as well, so an
/// outlet this list has not met yet but that names itself for what it sells is still caught. Generic
/// words such as "auction" or "export" alone never match: Manheim and ADESA run dealer-only auctions,
/// and an ordinary exporter is not a salvage outlet.
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
        "Salvage Autos Auction",
        "Ridesafely",
        "Bid N Drive",
        "Auto4export",
    ];

    private static readonly IReadOnlyList<string> KeywordWords = ["salvage", "repairable", "repairables"];

    private static readonly IReadOnlyList<string[]> OutletWords = [.. Outlets.Select(Words)];

    /// <summary>True when <paramref name="dealerName"/> names one of <see cref="Outlets"/>, meaning
    /// its words contain an outlet's words as a contiguous run, or when one of its words is "salvage",
    /// "repairable", or "repairables".</summary>
    public static bool IsSalvageSeller(string? dealerName)
    {
        if (string.IsNullOrWhiteSpace(dealerName))
        {
            return false;
        }

        string[] words = Words(dealerName);
        return words.Any(word => KeywordWords.Contains(word))
            || OutletWords.Any(outlet => ContainsRun(words, outlet));
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
