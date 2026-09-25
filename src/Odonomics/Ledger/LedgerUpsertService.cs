using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

public sealed record UpsertOutcome(bool VehicleIsNew, bool PostingIsNew, bool PriceChanged, decimal? PreviousPrice);

/// <summary>
/// Upsert semantics: a vehicle seen again updates its last-known year/make/model/trim/mileage and
/// LastSeen; a posting seen again updates LastSeen and appends a PriceObservation only when the
/// price actually changed (or this is the posting's first sighting), and takes the candidate's
/// shipping fee as the posting's latest. Nothing is ever deleted.
/// </summary>
public sealed class LedgerUpsertService(OdonomicsDbContext db)
{
    public async Task<UpsertOutcome> UpsertAsync(ListingCandidate candidate, RunEntity run, CancellationToken cancellationToken)
    {
        DateTimeOffset now = run.StartedAt;

        VehicleEntity? vehicle = await db.Vehicles.FindAsync([candidate.Vin], cancellationToken);
        bool vehicleIsNew = vehicle is null;
        if (vehicle is null)
        {
            vehicle = new VehicleEntity
            {
                Vin = candidate.Vin,
                Year = candidate.Year,
                Make = candidate.Make,
                Model = candidate.Model,
                Trim = candidate.Trim,
                Mileage = candidate.Mileage,
                FirstSeen = now,
                LastSeen = now,
            };
            db.Vehicles.Add(vehicle);
        }
        else
        {
            vehicle.Year = candidate.Year;
            vehicle.Make = candidate.Make;
            vehicle.Model = candidate.Model;
            vehicle.Trim = candidate.Trim;
            vehicle.Mileage = candidate.Mileage;
            vehicle.LastSeen = now;
        }

        PostingEntity? posting = await db.Postings
            .Include(p => p.PriceObservations)
            .Include(p => p.Dealer)
            .Where(p => p.VehicleVin == candidate.Vin && p.Source == candidate.Source && p.Url == candidate.Url)
            .FirstOrDefaultAsync(cancellationToken);

        DealerEntity? dealer = candidate.DealerNameIsFallback
            ? await ResolveFallbackDealerAsync(candidate, posting, now, cancellationToken)
            : await FindOrCreateDealerAsync(candidate.DealerName, candidate.DealerLocation, cancellationToken);

        bool postingIsNew = posting is null;
        decimal? previousPrice = null;
        bool priceChanged;

        if (posting is null)
        {
            posting = new PostingEntity
            {
                VehicleVin = candidate.Vin,
                Source = candidate.Source,
                Url = candidate.Url,
                FirstSeen = now,
                LastSeen = now,
            };
            db.Postings.Add(posting);
            priceChanged = true; // the first sighting is always recorded
        }
        else
        {
            posting.LastSeen = now;
            PriceObservationEntity? latest = posting.PriceObservations
                .OrderByDescending(o => o.ObservedAt)
                .FirstOrDefault();
            previousPrice = latest?.Price;
            priceChanged = latest is null || latest.Price != candidate.Price;
        }

        // Only ever links a posting to a dealer, never clears one: a later sighting whose source
        // didn't carry dealer info (candidate.DealerName null) must not erase a link an earlier,
        // more informative sighting already established.
        if (dealer is not null)
        {
            posting.Dealer = dealer;
        }

        // The latest sighting's fee replaces the last one, unlike the dealer link above: a fee is
        // per car and per buyer zip, so an older figure is stale rather than more informative.
        posting.ShippingFee = candidate.ShippingFee;

        if (priceChanged)
        {
            posting.PriceObservations.Add(new PriceObservationEntity
            {
                PostingId = posting.Id,
                Price = candidate.Price,
                ObservedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new UpsertOutcome(vehicleIsNew, postingIsNew, priceChanged && !postingIsNew, previousPrice);
    }

    /// <summary>The dealer a sighting whose source named none (<see cref="ListingCandidate.DealerNameIsFallback"/>,
    /// carvana's "Carvana") links its posting to, or null to leave the posting's link as it is. The
    /// VIN history the ledger already stores may name the hub the car sits in for this posting's
    /// window (see <see cref="CarvanaDealers.HubNameFromHistory"/>): a posting with no dealer, or one
    /// on a "Carvana" row, moves to that hub. Otherwise a posting with no dealer gets the fallback
    /// dealer, and a posting that already has one keeps it: a fallback name says the source named no
    /// dealer, so it must not replace a link to a real one, nor mint a dealer row a posting will
    /// never use.</summary>
    private async ValueTask<DealerEntity?> ResolveFallbackDealerAsync(ListingCandidate candidate, PostingEntity? posting, DateTimeOffset now, CancellationToken cancellationToken)
    {
        bool mayMove = posting?.Dealer is null || CarvanaDealers.IsChain(posting.Dealer);
        if (!mayMove)
        {
            return null;
        }

        string? historyJson = await db.VinRecords
            .Where(r => r.Vin == candidate.Vin)
            .Select(r => r.HistoryRawJson)
            .FirstOrDefaultAsync(cancellationToken);
        string? hubName = CarvanaDealers.HubNameFromHistory(historyJson, posting?.FirstSeen ?? now, now);
        if (hubName is not null)
        {
            return await FindOrCreateDealerAsync(hubName, null, cancellationToken);
        }

        return posting?.Dealer is null
            ? await FindOrCreateDealerAsync(candidate.DealerName, candidate.DealerLocation, cancellationToken)
            : null;
    }

    /// <summary>Finds the dealer matching <paramref name="dealerName"/> and
    /// <paramref name="dealerLocation"/> by their normalized form, or creates one. Returns null
    /// when the candidate carries no dealer name at all, the common case for a source that hasn't
    /// exposed one.</summary>
    private async Task<DealerEntity?> FindOrCreateDealerAsync(string? dealerName, string? dealerLocation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(dealerName))
        {
            return null;
        }

        // A Carvana seller is keyed by its name alone, so a walk's pickup city or an API's city for the
        // same seller can't split "Carvana" or one hub into a row per location (see CarvanaDealers).
        if (CarvanaDealers.IsChainOrHubName(dealerName))
        {
            dealerLocation = null;
        }

        string normalizedName = DealerNormalizer.Normalize(dealerName);
        string normalizedLocation = DealerNormalizer.NormalizeLocation(dealerLocation);

        DealerEntity? dealer = await db.Dealers
            .FirstOrDefaultAsync(d => d.NormalizedName == normalizedName && d.NormalizedLocation == normalizedLocation, cancellationToken);
        if (dealer is not null)
        {
            return dealer;
        }

        dealer = new DealerEntity
        {
            Name = dealerName.Trim(),
            Location = string.IsNullOrWhiteSpace(dealerLocation) ? null : dealerLocation.Trim(),
            NormalizedName = normalizedName,
            NormalizedLocation = normalizedLocation,
        };
        db.Dealers.Add(dealer);
        return dealer;
    }
}
