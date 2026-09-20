using Microsoft.EntityFrameworkCore;

namespace Odonomics.Ledger;

public sealed record UpsertOutcome(bool VehicleIsNew, bool PostingIsNew, bool PriceChanged, decimal? PreviousPrice);

/// <summary>
/// Upsert semantics: a vehicle seen again updates its last-known year/make/model/trim/mileage and
/// LastSeen; a posting seen again updates LastSeen and appends a PriceObservation only when the
/// price actually changed (or this is the posting's first sighting). Nothing is ever deleted.
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
            .Where(p => p.VehicleVin == candidate.Vin && p.Source == candidate.Source && p.Url == candidate.Url)
            .FirstOrDefaultAsync(cancellationToken);

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
}
