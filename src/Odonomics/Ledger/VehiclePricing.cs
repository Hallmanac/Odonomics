using Odonomics.Domain;
using Odonomics.Walk;

namespace Odonomics.Ledger;

public static class VehiclePricing
{
    /// <summary>The lowest latest price among this vehicle's postings that were still active as of
    /// the most recent full coverage of that posting's own source and model (LastSeen is that run's
    /// timestamp or later; see LedgerUpsertService and <see cref="RunSources.LatestCoverageBySource"/>).
    /// A posting whose source/model no run has ever covered, or that the latest full coverage saw, still
    /// counts, and so does one a later capped walk reached or never reached, or one a run's render wait
    /// gave up on while the ledger already held it (<see cref="PostingEntity.CardUnrenderedSeenAt"/> at or
    /// after the latest full coverage); only a posting whose source/model was fully checked more recently
    /// without seeing it again, or without its card failing to render again, drops out. A cars.com posting
    /// for one of CarMax's own stores never counts as active at all, however recently it was seen (see
    /// <see cref="IsCarMaxDealerOnCarsCom"/>): CarMax is walked nationwide by its own site, so the vehicle
    /// stands on its CarMax posting and any non-CarMax postings instead, never on a redundant cars.com copy.
    /// This is the asking price alone, which is what the price red flags compare; the cost model
    /// uses <see cref="LowestCurrentPurchasePrice"/>.</summary>
    public static decimal? LowestCurrentPrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        decimal?[] known = [.. ActivePostings(vehicle, latestCoverageBySource).Select(LatestAskingPrice).Where(p => p is not null)];
        return known.Length == 0 ? null : known.Min();
    }

    /// <summary>The cheapest way to take this vehicle home among its active, purchasable postings (see
    /// <see cref="LowestCurrentPrice"/> for which are active; a posting currently reserved or in transit,
    /// see <see cref="IsReservedOrInTransit"/>, or a CarMax "Only at" posting whose store sits outside
    /// <paramref name="radiusMiles"/> of <paramref name="zip"/>, see <see cref="IsOnlyAtOutOfRadius"/>, is
    /// never a candidate here even when it is the cheapest priced one, since it cannot actually be bought
    /// right now): each posting's latest asking price plus the fee for <paramref name="fulfillment"/>,
    /// plus the fees it itemized on top of that price when its fee posture is itemized, so a far-away
    /// carvana car, or a dealer whose fees are added at the desk, is compared honestly with one that has
    /// neither. Under delivery the fee is the shipping fee; under pickup it is the pickup fee, or the
    /// shipping fee when no pickup fee was read (see <see cref="PurchasePrice"/>). A posting with a null
    /// fee costs its asking price, and so does one whose posture is all-in (its price already holds its
    /// fees, and any shipping fee it shows), unknown (the page did not say), or never read. When two
    /// postings cost the same to take home, the one with the lower asking price is reported.
    /// <paramref name="zip"/> and <paramref name="radiusMiles"/> are the scenario's own; null when no
    /// scenario is in play (a bare VIN lookup), which never treats an "Only at" posting as out of radius,
    /// same as before this existed.</summary>
    public static PurchasePrice? LowestCurrentPurchasePrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment, string? zip = null, int? radiusMiles = null) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment, zip, radiusMiles)?.Price;

    /// <summary>The active, purchasable posting <see cref="LowestCurrentPurchasePrice"/> reports the price
    /// of, so a display that sits beside that price (a site's own badge for the listing, its dealer and fee
    /// posture) speaks for the same listing; null when no active, purchasable posting has a price.</summary>
    public static PostingEntity? LowestCurrentPurchasePosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment, string? zip = null, int? radiusMiles = null) =>
        CheapestPosting(vehicle, latestCoverageBySource, fulfillment, zip, radiusMiles)?.Posting;

    /// <summary>The price the cheapest active, non-reserved posting would cost to take home if an
    /// out-of-radius CarMax "Only at" posting still counted as purchasable, for a price-ceiling check that
    /// must still reject a car that is too expensive even though <see cref="OnlyAtOutOfRadiusStore"/> would
    /// otherwise make <see cref="LowestCurrentPurchasePrice"/> null for it (see
    /// <see cref="Cli.Commands.ResearchCommand"/>'s own use of this: it tolerates the out-of-radius reason so
    /// it still researches a car in that shape, and researching it anyway must not also skip the price
    /// ceiling that shape happens to silence). A reserved or in-transit posting is still excluded, the same
    /// as <see cref="LowestCurrentPurchasePrice"/>.</summary>
    public static PurchasePrice? LowestPriceIncludingOutOfRadius(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment)
    {
        (PostingEntity Posting, PurchasePrice Price)[] known =
        [
            .. ActivePostings(vehicle, latestCoverageBySource)
                .Where(p => !IsReservedOrInTransit(p))
                .Select(p => (Posting: p, Asking: LatestAskingPrice(p)))
                .Where(p => p.Asking is not null)
                .Select(p => (p.Posting, PurchasePriceOf(p.Posting, p.Asking.GetValueOrDefault(), fulfillment))),
        ];

        return known.Length == 0
            ? null
            : known.OrderBy(p => p.Price.Total).ThenBy(p => p.Price.Asking).First().Price;
    }

    private static (PostingEntity Posting, PurchasePrice Price)? CheapestPosting(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, Fulfillment fulfillment, string? zip, int? radiusMiles)
    {
        (PostingEntity Posting, PurchasePrice Price)[] known =
        [
            .. ActivePostings(vehicle, latestCoverageBySource)
                .Where(p => IsPurchasable(p, zip, radiusMiles))
                .Select(p => (Posting: p, Asking: LatestAskingPrice(p)))
                .Where(p => p.Asking is not null)
                .Select(p => (p.Posting, PurchasePriceOf(p.Posting, p.Asking.GetValueOrDefault(), fulfillment))),
        ];

        return known.Length == 0
            ? null
            : known.OrderBy(p => p.Price.Total).ThenBy(p => p.Price.Asking).First();
    }

    /// <summary>The posting's purchase price at <paramref name="asking"/>. Itemized fees count only when
    /// the posture is itemized: an all-in page's asking price already holds its fees, and adding an
    /// itemized total the page also printed would count them twice. That total is still carried, for
    /// display, as the fees an all-in price includes. A shipping fee on an all-in posting is only ever
    /// carried for display and never added when the posting is cargurus's own: the only card that says so
    /// is one that says "Price includes $462 shipping" (see <see cref="Walk.FeeStatements.ReadCarGurusCard"/>),
    /// so that is the only source whose all-in posture says anything about shipping at all. Every other
    /// site's all-in posture (cars.com's "Seller has no extra fees", Autotrader's "Dealer Fees Included")
    /// speaks only to the dealer's own documentation fees; a stored shipping or delivery fee on one of
    /// those postings is a separate charge the page never said was in the price, so it is still added (see
    /// <see cref="PurchasePrice.ShippingIncluded"/>). A CarMax "Only at" posting's own $0
    /// <see cref="PostingEntity.ShippingFee"/> is read here as no fee at all rather than a free one: it is a
    /// side effect of the same "Available today" card line an ordinary local pickup city also sets it from
    /// (see <see cref="CarMaxStores.OnlyAtStoreName"/>), and CarMax offers no delivery on a car like this at
    /// all, only pickup at its one store, so treating it as free shipping would tell a buyer under delivery
    /// fulfillment that CarMax will ship a car it will not.</summary>
    private static PurchasePrice PurchasePriceOf(PostingEntity posting, decimal asking, Fulfillment fulfillment)
    {
        decimal? shippingFee = CarMaxStores.OnlyAtStoreName(posting.PickupLocation) is not null ? null : posting.ShippingFee;
        bool shippingIncluded = posting.Source == WalkSites.CarGurus.Name && shippingFee is not null;
        return posting.FeePosture switch
        {
            FeePostures.Itemized => new(asking, shippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, posting.ItemizedFeesTotal, posting.FeePosture),
            FeePostures.AllIn => new(asking, shippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture, posting.ItemizedFeesTotal, ShippingIncluded: shippingIncluded),
            _ => new(asking, shippingFee, posting.PickupFee, posting.PickupLocation, fulfillment, null, posting.FeePosture),
        };
    }

    /// <summary>True when this vehicle has at least one active posting (see <see cref="LowestCurrentPrice"/>
    /// for which those are) currently carrying CarMax's own <see cref="PostingAttributeNames.Availability"/>
    /// attribute ("Reserved for another buyer" or "In transit, not yet purchasable"), and none of its active
    /// postings is both purchasable (see <see cref="IsPurchasable"/>) and priced: whether because every active
    /// posting is reserved, in transit, or an out-of-radius "Only at" posting, or because the only ones that
    /// aren't have no valid price to rank or budget on (see <see cref="LatestAskingPrice"/>) and so are
    /// indistinguishable from gone. Either way <see cref="CheapestPosting"/> has nothing to price the vehicle
    /// from, so this is the reason to give instead of ranking it on the reserved posting's own price. A
    /// vehicle with no active posting at all (every posting gone) is false here too, since that is
    /// <see cref="Domain.Scorer.FilterReasons"/>'s own separate reason to give, not this one's. A vehicle
    /// excluded only for the out-of-radius "Only at" reason is also false here: <see cref="OnlyAtOutOfRadiusStore"/>
    /// is what gives that reason instead.</summary>
    public static bool OnlyReservedOrInTransit(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, string? zip = null, int? radiusMiles = null)
    {
        PostingEntity[] active = [.. ActivePostings(vehicle, latestCoverageBySource)];
        return active.Length > 0
            && active.Any(IsReservedOrInTransit)
            && !active.Any(p => IsPurchasable(p, zip, radiusMiles) && LatestAskingPrice(p) is not null);
    }

    /// <summary>The store to blame when this vehicle is excluded because every one of its active postings
    /// is either reserved, in transit, or a CarMax "Only at" posting whose store sits outside
    /// <paramref name="radiusMiles"/> of <paramref name="zip"/> (see <see cref="IsOnlyAtOutOfRadius"/>),
    /// with at least one of them an out-of-radius "Only at" posting specifically: null otherwise, whether
    /// because some active posting is purchasable and priced (an in-radius "Only at" posting stays ranked
    /// off its own price, and a vehicle with another purchasable posting still prices from that one), because
    /// the vehicle has no active posting at all (<see cref="Domain.Scorer.FilterReasons"/>'s own "every
    /// posting gone" reason covers that), or because every active posting is reserved or in transit with none
    /// of them an "Only at" posting (<see cref="OnlyReservedOrInTransit"/> covers that, with nothing here to
    /// find). A vehicle whose active postings mix a reserved one with an out-of-radius "Only at" one and
    /// nothing purchasable makes both this and <see cref="OnlyReservedOrInTransit"/> true at once;
    /// <see cref="Domain.Scorer.FilterReasons"/> checks this one first, so that vehicle is excluded with this
    /// reason. <paramref name="zip"/> or <paramref name="radiusMiles"/> null (no scenario in play) always
    /// reads as null here, the same as <see cref="CheapestPosting"/>.</summary>
    public static string? OnlyAtOutOfRadiusStore(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, string? zip, int? radiusMiles)
    {
        PostingEntity[] active = [.. ActivePostings(vehicle, latestCoverageBySource)];
        if (active.Length == 0 || active.Any(p => IsPurchasable(p, zip, radiusMiles) && LatestAskingPrice(p) is not null))
        {
            return null;
        }

        return active
            .Where(p => !IsReservedOrInTransit(p))
            .Select(p => CarMaxStores.OnlyAtStoreName(p.PickupLocation))
            .FirstOrDefault(store => store is not null && IsOutOfRadius(store, zip, radiusMiles));
    }

    /// <summary>The store named by any of this vehicle's active, non-reserved postings' CarMax "Only at"
    /// pickup location whose distance from <paramref name="zip"/> <see cref="CarMaxStores.DistanceMilesFromZip"/>
    /// could not measure, because the store or the zip is not in that file's own curated tables: that gap
    /// makes <see cref="IsOutOfRadius"/> read as false, so this vehicle counts as purchasable and prices
    /// normally off that posting on nothing more than the absence of a measurement, not a confirmed in-radius
    /// distance. Null when <paramref name="zip"/> or <paramref name="radiusMiles"/> is null (no scenario is in
    /// play, so no distance is ever checked), when no active posting names an "Only at" store, or when every
    /// one named is a store and zip this file's tables do know. `odo rank` and `odo show` surface this as a
    /// caveat beside the vehicle rather than silently trusting the unmeasured distance.</summary>
    public static string? UnmeasuredOnlyAtStore(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource, string? zip, int? radiusMiles)
    {
        if (zip is null || radiusMiles is null)
        {
            return null;
        }

        return ActivePostings(vehicle, latestCoverageBySource)
            .Where(p => !IsReservedOrInTransit(p))
            .Select(p => CarMaxStores.OnlyAtStoreName(p.PickupLocation))
            .FirstOrDefault(store => store is not null && CarMaxStores.DistanceMilesFromZip(store, zip) is null);
    }

    /// <summary>True when <paramref name="posting"/> is neither reserved/in-transit nor a CarMax "Only at"
    /// posting whose store sits outside <paramref name="radiusMiles"/> of <paramref name="zip"/>: the one
    /// test both <see cref="CheapestPosting"/> and the "only every posting is X" checks above share for
    /// "can this posting actually be bought right now".</summary>
    private static bool IsPurchasable(PostingEntity posting, string? zip, int? radiusMiles) =>
        !IsReservedOrInTransit(posting) && !IsOnlyAtOutOfRadius(posting, zip, radiusMiles);

    /// <summary>True when the posting's <see cref="PostingEntity.PickupLocation"/> names a CarMax "Only at"
    /// store (see <see cref="CarMaxStores.OnlyAtStoreName"/>) that sits farther than
    /// <paramref name="radiusMiles"/> from <paramref name="zip"/>, per <see cref="CarMaxStores.DistanceMilesFromZip"/>.
    /// False when the posting names no such store, when either <paramref name="zip"/> or
    /// <paramref name="radiusMiles"/> is null (no scenario in play), or when the store or zip is one this
    /// file's own curated location tables don't know: a distance nobody actually measured never excludes a
    /// posting.</summary>
    private static bool IsOnlyAtOutOfRadius(PostingEntity posting, string? zip, int? radiusMiles) =>
        CarMaxStores.OnlyAtStoreName(posting.PickupLocation) is string store && IsOutOfRadius(store, zip, radiusMiles);

    private static bool IsOutOfRadius(string store, string? zip, int? radiusMiles) =>
        zip is not null
        && radiusMiles is int radius
        && CarMaxStores.DistanceMilesFromZip(store, zip) is double miles
        && miles > radius;

    /// <summary>The <see cref="PostingAttributeNames.Availability"/> note of the first active posting that
    /// carries one, even when a different, purchasable posting is the one <see cref="LowestCurrentPurchasePosting"/>
    /// actually prices the vehicle from: informational only, for `odo rank` to show beside a row that still
    /// ranks normally off another posting. <see cref="OnlyReservedOrInTransit"/> is what actually excludes a
    /// vehicle; null when no active posting carries the attribute.</summary>
    public static string? ReservedOrInTransitNote(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        ActivePostings(vehicle, latestCoverageBySource)
            .SelectMany(p => p.Attributes)
            .FirstOrDefault(a => a.Name == PostingAttributeNames.Availability)?.Value;

    /// <summary>True when the posting currently carries CarMax's <see cref="PostingAttributeNames.Availability"/>
    /// attribute at all: the attribute is only ever set to say a car can't be bought right now (see its own
    /// doc comment), and a run whose detail visit finds the reservation lifted removes the row entirely
    /// rather than changing its value (see <see cref="LedgerUpsertService.ApplyAttributes"/> and
    /// <see cref="PostingEntity.AvailabilityClearedAt"/>), so the row's mere presence is enough.</summary>
    private static bool IsReservedOrInTransit(PostingEntity posting) =>
        posting.Attributes.Any(a => a.Name == PostingAttributeNames.Availability);

    private static IEnumerable<PostingEntity> ActivePostings(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        vehicle.Postings.Where(p =>
        {
            if (IsCarMaxDealerOnCarsCom(p))
            {
                return false;
            }

            string key = RunSources.Key(p.Source, vehicle.Model);
            return !latestCoverageBySource.TryGetValue(key, out DateTimeOffset latestCoverage)
                || p.LastSeen >= latestCoverage
                || p.CardUnrenderedSeenAt >= latestCoverage;
        });

    /// <summary>True when <paramref name="posting"/> is a cars.com posting for one of CarMax's own
    /// stores: cars.com carries CarMax's whole nationwide inventory on its own search results (a
    /// "$1,999 delivery to Orlando, FL (14 mi)" card for a car thousands of miles off), which
    /// CarMax's own walk already covers end to end, and cars.com's delivery offer does not actually
    /// hold for a car CarMax will only sell "Only at" its one store (see
    /// <see cref="Walk.CarMaxStores.OnlyAtStoreName"/>). Going forward the cars.com walk skips a
    /// posting like this outright (see <see cref="WalkSite.IsCarMaxDealer"/> and
    /// <see cref="Cli.Commands.WalkCommand"/>'s own use of it) rather than saving it, but a posting
    /// already on the ledger from before that skip existed needs excluding here too, on every run,
    /// not just once it goes stale.</summary>
    private static bool IsCarMaxDealerOnCarsCom(PostingEntity posting) =>
        posting.Source == WalkSites.CarsCom.Name && WalkSites.CarsCom.IsCarMaxDealer(posting.Dealer?.Name);

    /// <summary>True when this vehicle has no active posting at all (see <see cref="ActivePostings"/>) only
    /// because its only posting(s) are cars.com copies of one of CarMax's own stores (see
    /// <see cref="IsCarMaxDealerOnCarsCom"/>), which <see cref="ActivePostings"/> always drops: the car is
    /// not actually gone, it is simply not priced from a copy the CarMax walk already covers. <see
    /// cref="Domain.Scorer.FilterReasons"/> gives this its own reason instead of "every posting is gone" when
    /// it applies, and <see cref="Cli.Commands.ShowCommand"/> does the same. False when the vehicle has any
    /// active posting of its own (whatever else the CarMax-on-cars.com exclusion also drops from it doesn't
    /// matter then), or when none of its raw postings is a cars.com CarMax dealer posting at all.</summary>
    public static bool OnlyCarsComCarMaxPostings(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource) =>
        !ActivePostings(vehicle, latestCoverageBySource).Any() && vehicle.Postings.Any(IsCarMaxDealerOnCarsCom);

    /// <summary>The posting's most recent asking price, or null when that price is below
    /// <see cref="PlaceholderPrice.Floor"/>: a placeholder already on the ledger is history, never
    /// a price to rank or budget on, and it does not fall back to an older observation.</summary>
    private static decimal? LatestAskingPrice(PostingEntity posting) =>
        posting.PriceObservations
            .OrderByDescending(o => o.ObservedAt)
            .Select(o => (decimal?)o.Price)
            .FirstOrDefault() is decimal latest && !PlaceholderPrice.IsBelowFloor(latest)
            ? latest
            : null;
}
