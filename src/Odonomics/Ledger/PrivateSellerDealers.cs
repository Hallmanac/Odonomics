using Odonomics.Walk;

namespace Odonomics.Ledger;

/// <summary>What the ledger knows about private sellers. A private seller is a person, not a business,
/// so every one of them is stored under the single dealer name <see cref="WalkSites.PrivateSellerDealerName"/>
/// with no location (see <see cref="WalkSite.ResolveDealer"/>); CarEdge has no rating to look up for
/// that name, so <c>odo dealer grade</c> skips the row rather than asking CarEdge about it.</summary>
public static class PrivateSellerDealers
{
    /// <summary>The normalized dealer name every private seller's row carries (see <see cref="DealerNormalizer"/>).</summary>
    public static readonly string NormalizedName = DealerNormalizer.Normalize(WalkSites.PrivateSellerDealerName);

    public static bool IsPrivateSeller(DealerEntity dealer) => dealer.NormalizedName == NormalizedName;
}
