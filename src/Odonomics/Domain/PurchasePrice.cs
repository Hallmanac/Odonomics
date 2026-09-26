namespace Odonomics.Domain;

/// <summary>What a car costs to take home before tax and fees: the asking price plus the one-time fee
/// for the way the buyer takes it home (<see cref="Fulfillment"/>), plus the fees the listing itemized on
/// top of its price when it says they are not in it (see <see cref="ItemizedFees"/>). Under delivery the
/// one-time fee is the shipping fee when the listing shows one (carvana); under pickup it is the pickup
/// fee, and a listing whose pickup fee was never read is assumed to cost its shipping fee, since nothing
/// says pickup is cheaper. A null fee means the listing showed none, and adds nothing; a fee of 0 means it
/// was free. Every purchase-price figure the cost model uses is <see cref="Total"/>; the asking price alone
/// still feeds the price red flags, which compare it with other listings' asking prices.</summary>
/// <param name="ItemizedFees">The sum of the fee lines the listing itemized on top of its asking price;
/// null when it itemized none, and always null for an all-in or unknown listing, which adds nothing
/// because its asking price already holds its fees or the page did not say.</param>
/// <param name="FeePosture">How the listing says its price relates to its fees (<c>all-in</c>,
/// <c>itemized</c>, <c>unknown</c>), for display; null when it was never read. It never changes the
/// total.</param>
public readonly record struct PurchasePrice(
    decimal Asking,
    decimal? ShippingFee,
    decimal? PickupFee = null,
    string? PickupLocation = null,
    Fulfillment Fulfillment = Fulfillment.Delivery,
    decimal? ItemizedFees = null,
    string? FeePosture = null)
{
    /// <summary>The fee the chosen <see cref="Fulfillment"/> adds, or null when the listing gives none.</summary>
    public decimal? AppliedFee => Fulfillment switch
    {
        Fulfillment.Pickup => PickupFee ?? ShippingFee,
        _ => ShippingFee,
    };

    /// <summary>True when the fulfillment is pickup but no pickup fee was read, so the shipping fee
    /// stands in for it.</summary>
    public bool PickupFeeAssumed => Fulfillment == Fulfillment.Pickup && PickupFee is null && ShippingFee is not null;

    public decimal Total => Asking + (AppliedFee ?? 0m) + (ItemizedFees ?? 0m);
}
