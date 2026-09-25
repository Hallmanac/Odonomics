namespace Odonomics.Ledger;

/// <summary>A fully-resolved candidate ready to reach the ledger: it always carries a VIN and a
/// URL, since a candidate missing either is rejected before it gets here (see IListingSource).</summary>
public sealed record ListingCandidate
{
    public required string Vin { get; init; }
    public required string Source { get; init; }
    public required string Url { get; init; }
    public required int Year { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public string? Trim { get; init; }
    public required decimal Price { get; init; }
    public required int Mileage { get; init; }
    public string? DealerName { get; init; }
    public string? DealerLocation { get; init; }

    /// <summary>True when <see cref="DealerName"/> is a site's stand-in for a page that named no
    /// dealer (carvana's "Carvana"), not a name the source gave. The upsert then links it only to a
    /// posting that has no dealer yet.</summary>
    public bool DealerNameIsFallback { get; init; }

    /// <summary>The one-time shipping fee the source's page showed on top of <see cref="Price"/>:
    /// 0 for free shipping, null when the source shows none (only carvana does).</summary>
    public decimal? ShippingFee { get; init; }
}
