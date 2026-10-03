using Odonomics.Domain;

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
    /// 0 for free shipping, null when the source shows none (carvana and carmax do).</summary>
    public decimal? ShippingFee { get; init; }

    /// <summary>The display-only facts the source showed about the listing, such as a deal badge or a
    /// dealer rating, keyed by the names in <see cref="PostingAttributeNames"/>. The upsert stores
    /// them as the posting's attributes (see <see cref="PostingAttributeEntity"/>); empty when the
    /// source showed none.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>();

    /// <summary>Names of display-only attributes this sighting's page proves no longer apply, so the
    /// upsert removes their stored rows outright rather than leaving them (see
    /// <see cref="LedgerUpsertService.ApplyAttributes"/>). Unlike <see cref="Attributes"/>, whose
    /// absence from a later sighting is never proof a badge is gone, a name here is a state the page's
    /// own text states positively has ended (CarMax's "availability" once a header stops reading
    /// "Reserved at" or "Coming to"), so it does not survive the way a card badge does. Empty when the
    /// source names none.</summary>
    public IReadOnlySet<string> AttributesToClear { get; init; } = new HashSet<string>();

    /// <summary>What it costs to pick the car up instead of having it delivered, when the source's page
    /// offers that (only carvana does). Null when it shows no pickup option, and 0 when the option
    /// prints no fee.</summary>
    public decimal? PickupFee { get; init; }

    /// <summary>Where the car can be picked up: carvana's pickup block, or the city a carmax car that is
    /// already at a store names ("Available today·Orlando"). Null when the source names none.</summary>
    public string? PickupLocation { get; init; }

    /// <summary>How the source's page says the price relates to the dealer's fees, one of the
    /// <see cref="FeePostures"/> strings: null when no reader for the source ran (carvana).</summary>
    public string? FeePosture { get; init; }

    /// <summary>The sum of the fee lines the page itemized: on top of <see cref="Price"/> when
    /// <see cref="FeePosture"/> is itemized, or already inside it when the posture is all-in; null
    /// when the page itemized none.</summary>
    public decimal? ItemizedFeesTotal { get; init; }

    /// <summary>The title-brand phrase the page's own text stated, as read (see
    /// <see cref="Walk.TitleBrandStatements"/>); null when it stated none.</summary>
    public string? TitleBrandPhrase { get; init; }

    /// <summary>What the listing's window sticker or factory equipment list says about smart-key entry, when the
    /// page had one. Unknown when it had none (a dealer's description does not count), in which case the
    /// upsert leaves whatever an earlier sighting stored.</summary>
    public EquipmentStatus SmartKeyEntry { get; init; }

    /// <summary>What that same sticker says about push-button start, with the same rule as <see cref="SmartKeyEntry"/>.</summary>
    public EquipmentStatus PushButtonStart { get; init; }

    /// <summary>What that same sticker says about a remote keyless fob (its plain "Keyless Entry"), with the same
    /// rule as <see cref="SmartKeyEntry"/>.</summary>
    public EquipmentStatus KeylessFobEntry { get; init; }
}
