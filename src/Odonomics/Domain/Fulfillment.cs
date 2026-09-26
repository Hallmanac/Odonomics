namespace Odonomics.Domain;

/// <summary>How the buyer takes the car home, which decides which one-time fee joins its asking
/// price (see <see cref="PurchasePrice"/>). A scenario picks one; delivery is the default, and pickup
/// is the what-if for a buyer willing to collect the car.</summary>
public enum Fulfillment
{
    /// <summary>The seller ships the car to the buyer, so the posting's shipping fee counts.</summary>
    Delivery,

    /// <summary>The buyer collects the car, so the posting's pickup fee counts instead.</summary>
    Pickup,
}

public static class FulfillmentNames
{
    /// <summary>The lowercase word the scenario file, the <c>--fulfillment</c> option, and the printed
    /// figures all use.</summary>
    public static string Word(this Fulfillment fulfillment) => fulfillment switch
    {
        Fulfillment.Pickup => "pickup",
        _ => "delivery",
    };
}
