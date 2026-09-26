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

    /// <summary>The asking price of the posting that is cheapest to take home (see
    /// <see cref="PurchasePrice"/>), null when every posting is gone.</summary>
    public required decimal? LowestCurrentPrice { get; init; }

    /// <summary>The one-time shipping fee of that same posting, null when it shows none.</summary>
    public decimal? ShippingFee { get; init; }

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

    /// <summary>What the vehicle costs to take home, the asking price plus its shipping fee, which is
    /// the price the cost model uses; null when there is no current asking price.</summary>
    public PurchasePrice? PurchasePrice => LowestCurrentPrice is decimal asking
        ? new PurchasePrice(asking, ShippingFee)
        : null;

    public string MakeModel => $"{Make} {Model}";
}
