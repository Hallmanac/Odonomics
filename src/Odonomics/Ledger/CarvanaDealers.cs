using System.Text.Json;
using Odonomics.Marketcheck;
using Odonomics.Walk;

namespace Odonomics.Ledger;

/// <summary>What the ledger knows about Carvana as a seller. Carvana is one chain with a hub in many
/// cities; the same car is sold by "Carvana" (the bare chain name, which the walk stores when a
/// detail page names no hub) or by a hub such as "Carvana Winder". A hub is a dealer row keyed by
/// its own name alone: the location a Carvana page prints is the buyer's pickup city, and an API
/// source's city for the same seller would split its row the same way, so
/// <see cref="LedgerUpsertService"/> keys a Carvana dealer on its name whatever the source says, and
/// the bare chain row is the only "Carvana" row.
/// The hub a car actually sits in is only known from the Marketcheck VIN history the ledger stores,
/// which <see cref="HubNameFromHistory"/> reads.</summary>
public static class CarvanaDealers
{
    /// <summary>How far outside a posting's own sighting window a history listing may sit and still
    /// count as the same stay: Marketcheck crawls on its own schedule, so its last-seen date for a
    /// listing routinely trails the walk that saw the same listing by about a day (the recorded
    /// ledger has cars still listed when walked whose last crawl was 25 hours earlier).</summary>
    private static readonly TimeSpan WindowTolerance = TimeSpan.FromDays(2);

    /// <summary>The chain's normalized dealer name, the one both the bare row and any legacy located
    /// row carry (see <see cref="DealerNormalizer"/>).</summary>
    public static readonly string ChainNormalizedName = DealerNormalizer.Normalize(WalkSites.CarvanaDealerName);

    public static bool IsChain(DealerEntity dealer) => dealer.NormalizedName == ChainNormalizedName;

    /// <summary>True for the one bare "Carvana" row: the chain name with no location.</summary>
    public static bool IsBareChain(DealerEntity dealer) => IsChain(dealer) && dealer.NormalizedLocation == "";

    /// <summary>True when <paramref name="dealerName"/> names a Carvana hub: "Carvana" followed by
    /// something ("Carvana Winder"), so not the bare chain name itself.</summary>
    public static bool IsHubName(string? dealerName) =>
        DealerNormalizer.Normalize(dealerName).StartsWith(ChainNormalizedName + " ", StringComparison.Ordinal);

    /// <summary>True when <paramref name="dealerName"/> is the chain name or a hub's: a seller whose
    /// row is keyed by that name alone (see the class remarks).</summary>
    public static bool IsChainOrHubName(string? dealerName) =>
        DealerNormalizer.Normalize(dealerName) == ChainNormalizedName || IsHubName(dealerName);

    /// <summary>The Carvana hub the VIN history names for a posting seen from
    /// <paramref name="postingFirstSeen"/> to <paramref name="postingLastSeen"/>, or null when it
    /// names none for that window. A VIN's history lists every stay it has had, often at different
    /// hubs, so only a hub listing whose dates overlap the posting's sighting window (give or take
    /// <see cref="WindowTolerance"/>) counts, and the most recent such listing wins. A listing with
    /// no date on one end is open on that end. History that is missing or unreadable names no
    /// hub.</summary>
    public static string? HubNameFromHistory(string? historyRawJson, DateTimeOffset postingFirstSeen, DateTimeOffset postingLastSeen)
    {
        if (string.IsNullOrWhiteSpace(historyRawJson))
        {
            return null;
        }

        List<VinHistoryListing> listings;
        try
        {
            listings = JsonSerializer.Deserialize<List<VinHistoryListing>>(historyRawJson) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }

        return listings
            .Where(l => IsHubName(l.Dealer)
                && (l.FirstSeen is not DateTimeOffset firstSeen || firstSeen - WindowTolerance <= postingLastSeen)
                && (l.LastSeen is not DateTimeOffset lastSeen || lastSeen + WindowTolerance >= postingFirstSeen))
            .OrderByDescending(l => l.LastSeen ?? DateTimeOffset.MaxValue)
            .ThenByDescending(l => l.FirstSeen ?? DateTimeOffset.MinValue)
            .Select(l => l.Dealer?.Trim())
            .FirstOrDefault();
    }
}
