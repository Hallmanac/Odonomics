namespace Odonomics.Domain;

/// <summary>What a car costs to take home before tax and fees: the asking price plus the one-time fee
/// for the way the buyer takes it home (<see cref="Fulfillment"/>). Under delivery that is the shipping
/// fee when the listing shows one (carvana); under pickup it is the pickup fee, and a listing whose
/// pickup fee was never read is assumed to cost its shipping fee, since nothing says pickup is cheaper.
/// A null fee means the listing showed none, and adds nothing; a fee of 0 means it was free. Every
/// purchase-price figure the cost model uses is <see cref="Total"/>; the asking price alone still feeds
/// the price red flags, which compare it with other listings' asking prices.</summary>
public readonly record struct PurchasePrice(
    decimal Asking,
    decimal? ShippingFee,
    decimal? PickupFee = null,
    string? PickupLocation = null,
    Fulfillment Fulfillment = Fulfillment.Delivery)
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

    public decimal Total => Asking + (AppliedFee ?? 0m);
}
