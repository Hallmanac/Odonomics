namespace Odonomics.Domain;

/// <summary>The slice of a ledger vehicle the scorer needs; kept independent of the EF Core
/// entities so Domain has no dependency on Ledger.</summary>
public sealed record VehicleForScoring
{
    public required string Vin { get; init; }
    public required int Year { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int Mileage { get; init; }

    /// <summary>The asking price of the cheapest purchasable, priced posting to take home (see
    /// <see cref="PurchasePrice"/>), null when every posting is gone or, same as null, when none of
    /// the live ones is both purchasable and priced (see <see cref="OnlyReservedOrInTransit"/> below,
    /// which tells the two apart for <see cref="Scorer.FilterReasons"/>).</summary>
    public required decimal? LowestCurrentPrice { get; init; }

    /// <summary>The one-time shipping fee of that same posting, null when it shows none.</summary>
    public decimal? ShippingFee { get; init; }

    /// <summary>True when that shipping fee is already in the asking price (see
    /// <see cref="PurchasePrice.ShippingIncluded"/>).</summary>
    public bool ShippingIncluded { get; init; }

    /// <summary>The pickup fee and location of that same posting, null when its page showed no pickup
    /// option.</summary>
    public decimal? PickupFee { get; init; }

    public string? PickupLocation { get; init; }

    /// <summary>Which of the two fees <see cref="PurchasePrice"/> adds: the scenario's choice.</summary>
    public Fulfillment Fulfillment { get; init; } = Fulfillment.Delivery;

    /// <summary>The fees that same posting itemized on top of its asking price, null when it
    /// itemized none (see <see cref="PurchasePrice.ItemizedFees"/>).</summary>
    public decimal? ItemizedFees { get; init; }

    /// <summary>That same posting's fee posture, for display only (see
    /// <see cref="PurchasePrice.FeePosture"/>).</summary>
    public string? FeePosture { get; init; }

    /// <summary>Distinct CarEdge grades among this vehicle's postings' dealers, joined with "/"
    /// (e.g. "A+" or "A+/F"); null when no posting has a graded dealer yet.</summary>
    public string? DealerGrade { get; init; }

    /// <summary>True when every one of this vehicle's postings resolves to a dealer graded F: the
    /// signal `odo rank` surfaces under its own warning heading. A posting with no known dealer,
    /// or an ungraded one, keeps this false rather than assuming the worst.</summary>
    public bool OnlyFGradedDealers { get; init; }

    /// <summary>What the cheapest posting's own site said about it, as short text for `odo rank` to
    /// show beside the row ("GrD 4.9": its deal badge and the dealer's rating on that site); null when
    /// the ledger holds neither. Display only: the scorer never reads it, so it cannot move the
    /// ranking or any cost figure.</summary>
    public string? SiteBadge { get; init; }

    /// <summary>What any of this vehicle's active postings' own site said about whether the car can
    /// actually be bought right now (CarMax's "Reserved for another buyer" or "In transit, not yet
    /// purchasable"), for `odo rank` to show on its own line under the row; null when none of them holds one.
    /// This can name a posting other than the one the vehicle is priced from (see
    /// <see cref="Ledger.VehiclePricing.LowestCurrentPurchasePosting"/>, which is never itself reserved or
    /// in transit when a purchasable, priced posting exists), so it is display only: a vehicle with this set
    /// still ranks and prices normally off its other, purchasable posting. <see cref="OnlyReservedOrInTransit"/>
    /// below is the field the scorer actually excludes a vehicle on.</summary>
    public string? Availability { get; init; }

    /// <summary>True when none of this vehicle's live postings is both purchasable (not reserved or in
    /// transit) and priced: either every live posting carries the CarMax availability note, or the ones that
    /// don't have no valid price to rank or budget on. <see cref="Scorer.FilterReasons"/> excludes a vehicle
    /// this is true for, the same as an over-ceiling price or every posting gone; a vehicle with at least one
    /// live, purchasable, priced posting keeps this false and ranks and prices normally from that posting,
    /// never from a reserved or in-transit one. A car whose reservation later clears is ranked again with no
    /// manual step: this is computed fresh from the ledger's current state every run, not cached from an
    /// earlier one.</summary>
    public bool OnlyReservedOrInTransit { get; init; }

    /// <summary>The CarMax store to blame when every one of this vehicle's live postings is either
    /// reserved/in-transit or a CarMax "Only at" posting whose store sits outside the scenario's radius,
    /// with none both purchasable and priced (see <see cref="Ledger.VehiclePricing.OnlyAtOutOfRadiusStore"/>):
    /// null otherwise, including when a live, purchasable "Only at" posting sits inside the radius (that one
    /// still prices and ranks the vehicle normally) or when another live posting is purchasable regardless of
    /// what this one says. <see cref="Scorer.FilterReasons"/> is what actually excludes a vehicle this is set
    /// for, with the reason "only at &lt;store&gt;, out of radius".</summary>
    public string? OnlyAtOutOfRadiusStore { get; init; }

    /// <summary>What the vehicle costs to take home, the asking price plus the fee for its
    /// <see cref="Fulfillment"/> and its itemized fees, which is the price the cost model uses; null
    /// when there is no current asking price.</summary>
    public PurchasePrice? PurchasePrice => LowestCurrentPrice is decimal asking
        ? new PurchasePrice(asking, ShippingFee, PickupFee, PickupLocation, Fulfillment, ItemizedFees, FeePosture, ShippingIncluded: ShippingIncluded)
        : null;

    public string MakeModel => $"{Make} {Model}";
}
