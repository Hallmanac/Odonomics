using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>The red flags a vehicle's own postings raise, as opposed to the ones its VIN history
/// raises (see <see cref="RedFlagsEvaluator.Evaluate"/>). They come from the ledger and need no
/// research fetch, so `odo rank` can show one for a vehicle nobody has researched yet.</summary>
public static class FeeRedFlags
{
    /// <summary>The add-ons-dealer flag (see <see cref="RedFlagsEvaluator.AddOnsDealer"/>) for the
    /// posting the vehicle is priced at under <paramref name="fulfillment"/> (see <see cref="VehiclePricing.LowestCurrentPurchasePosting"/>), as a
    /// list of zero or one so a caller can append it to the flags a research result already has. The
    /// posting must have its <see cref="PostingEntity.Dealer"/> loaded.</summary>
    public static IReadOnlyList<RedFlag> For(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment)
    {
        PostingEntity? cheapest = VehiclePricing.LowestCurrentPurchasePosting(vehicle, latestCoverageBySource, fulfillment);
        RedFlag? flag = cheapest is null
            ? null
            : RedFlagsEvaluator.AddOnsDealer(
                cheapest.FeePosture == FeePostures.Unknown,
                cheapest.Dealer?.Name,
                cheapest.Dealer?.DocFee,
                cheapest.Dealer?.AddOnsNote);

        return flag is null
            ? []
            : [flag];
    }
}
