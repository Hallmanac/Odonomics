namespace Odonomics.Domain;

/// <summary>What a car costs to take home before tax and fees: the asking price plus the one-time
/// shipping fee when the listing shows one (carvana). A null fee means the listing showed none, and
/// adds nothing; a fee of 0 means it shipped free. Every purchase-price figure the cost model uses
/// is <see cref="Total"/>; the asking price alone still feeds the price red flags, which compare it
/// with other listings' asking prices.</summary>
public readonly record struct PurchasePrice(decimal Asking, decimal? ShippingFee)
{
    public decimal Total => Asking + (ShippingFee ?? 0m);
}
