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

    /// <summary>The display-only facts the source showed about the listing, such as a deal badge or a
    /// dealer rating, keyed by the names in <see cref="PostingAttributeNames"/>. The upsert stores
    /// them as the posting's attributes (see <see cref="PostingAttributeEntity"/>); empty when the
    /// source showed none.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    /// <summary>What it costs to pick the car up instead of having it delivered, and where, when the
    /// source's page offers that (only carvana does). Both are null when it shows no pickup option,
    /// and the fee is 0 when the option prints none.</summary>
    public decimal? PickupFee { get; init; }

    public string? PickupLocation { get; init; }

    /// <summary>How the source's page says the price relates to the dealer's fees, one of the
    /// <see cref="FeePostures"/> strings: null when no reader for the source ran (carvana).</summary>
    public string? FeePosture { get; init; }

    /// <summary>The sum of the fee lines the page itemized on top of <see cref="Price"/>, when
    /// <see cref="FeePosture"/> is itemized; null otherwise.</summary>
    public decimal? ItemizedFeesTotal { get; init; }
}
