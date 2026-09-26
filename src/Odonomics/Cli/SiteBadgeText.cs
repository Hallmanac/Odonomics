using Odonomics.Ledger;

namespace Odonomics.Cli;

/// <summary>The short text `odo rank` shows for what the listing's own site said about it: its deal
/// badge, abbreviated, and the dealer's rating on that site when the card printed one ("GrD 4.9"). It
/// is display only. The site's price opinion is untested until ledger history shows whether badged
/// cars sell faster or drop less, so nothing here reaches the cost model or the ranking.</summary>
public static class SiteBadgeText
{
    /// <summary>What each abbreviation stands for, kept beside <see cref="DealAbbreviations"/> so the
    /// legend rank prints and the abbreviations cannot drift apart.</summary>
    public const string Legend = "GrD great deal, GD good deal, FD fair deal, GrP great price, GP good price.";

    private static readonly IReadOnlyDictionary<string, string> DealAbbreviations = new Dictionary<string, string>
    {
        ["Great Deal"] = "GrD",
        ["Good Deal"] = "GD",
        ["Fair Deal"] = "FD",
        ["Great Price"] = "GrP",
        ["Good Price"] = "GP",
    };

    /// <summary>The badge text for a posting's attributes, or null when they hold neither a deal badge
    /// this knows how to abbreviate nor a dealer rating.</summary>
    public static string? For(IEnumerable<PostingAttributeEntity> attributes)
    {
        List<PostingAttributeEntity> stored = [.. attributes];
        string? dealBadge = stored.FirstOrDefault(a => a.Name == PostingAttributeNames.Deal)?.Value;
        string? deal = dealBadge is not null && DealAbbreviations.TryGetValue(dealBadge, out string? abbreviation)
            ? abbreviation
            : null;
        string? rating = stored.FirstOrDefault(a => a.Name == PostingAttributeNames.DealerRating)?.Value;

        string text = string.Join(' ', new[] { deal, rating }.OfType<string>());
        return text.Length == 0 ? null : text;
    }
}
