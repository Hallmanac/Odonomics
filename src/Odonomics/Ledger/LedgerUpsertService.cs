using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;

namespace Odonomics.Ledger;

public sealed record UpsertOutcome(bool VehicleIsNew, bool PostingIsNew, bool PriceChanged, decimal? PreviousPrice);

/// <summary>What <see cref="LedgerUpsertService.TouchAsync"/> did: how many postings the ledger held at
/// the links it was given, and how many of them the touch appended a price observation to.</summary>
public sealed record TouchOutcome(int Found, int PriceChanged);

/// <summary>
/// Upsert semantics: a vehicle seen again updates its last-known year/make/model/trim/mileage and
/// LastSeen; a posting seen again updates LastSeen and appends a PriceObservation only when the
/// price actually changed (or this is the posting's first sighting), and takes the candidate's
/// shipping fee, pickup option and fee statement as the posting's latest, and sets any display-only attributes the
/// candidate carries. Nothing is ever deleted. A posting can also be touched without a detail visit
/// (see <see cref="TouchAsync"/>), from a search page's result card alone.
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
        posting.FeePosture = candidate.FeePosture;
        posting.ItemizedFeesTotal = candidate.ItemizedFeesTotal;

        // The pickup option is replaced the same way, for the same reason, and so is a page whose
        // block did not render: it leaves both null rather than keeping a figure from another day.
        posting.PickupFee = candidate.PickupFee;
        posting.PickupLocation = candidate.PickupLocation;

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

        if (candidate.Attributes.Count > 0)
        {
            await SetPostingAttributesAsync(posting.Id, candidate.Attributes, run, cancellationToken);
        }

        return new UpsertOutcome(vehicleIsNew, postingIsNew, priceChanged && !postingIsNew, previousPrice);
    }

    /// <summary>Sets the display-only attributes <paramref name="run"/> read from one posting's page
    /// (see <see cref="PostingAttributeEntity"/>), in one save so either every name lands or none does.
    /// A name the posting already holds takes the new value and the run's id, since a later observation
    /// replaces the earlier one; a name it does not hold gets a new row. A name this run did not read is
    /// left as it was, because a page that stops showing a badge is not proof the badge is gone. Names
    /// and values are trimmed, and a blank name or value is skipped, since an empty badge says nothing.
    /// <paramref name="run"/> must already be saved, and the posting must exist.</summary>
    public async Task SetPostingAttributesAsync(int postingId, IReadOnlyDictionary<string, string> attributes, RunEntity run, CancellationToken cancellationToken)
    {
        List<PostingAttributeEntity> existing = await db.PostingAttributes
            .Where(a => a.PostingId == postingId)
            .ToListAsync(cancellationToken);

        ApplyAttributes(postingId, existing, attributes, run);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sets <paramref name="attributesByUrl"/> on the postings of <paramref name="source"/> at
    /// each URL, the way <see cref="SetPostingAttributesAsync"/> does for one posting, in one save so
    /// either every posting's attributes land or none do. This is how a known posting touched from its
    /// search card (see <see cref="TouchAsync"/>) gets the badges the card showed without a detail
    /// visit. A URL that matches no posting writes nothing. <paramref name="run"/> must already be
    /// saved.</summary>
    public async Task SetPostingAttributesByUrlAsync(string source, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> attributesByUrl, RunEntity run, CancellationToken cancellationToken)
    {
        string[] urls = [.. attributesByUrl.Keys];
        List<PostingEntity> postings = await db.Postings
            .Include(p => p.Attributes)
            .Where(p => p.Source == source && urls.Contains(p.Url))
            .ToListAsync(cancellationToken);

        foreach (PostingEntity posting in postings)
        {
            ApplyAttributes(posting.Id, [.. posting.Attributes], attributesByUrl[posting.Url], run);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Sets the shipping fee and pickup city on the postings of <paramref name="source"/> at each
    /// URL of <paramref name="feesByUrl"/>, in one save so either every posting's fee lands or none does.
    /// This is how a known posting touched from its search card (see <see cref="TouchAsync"/>) gets the
    /// fee the card printed without a detail visit, replacing the last sighting's fee and pickup city the
    /// way an upsert does. A URL that matches no posting writes nothing.</summary>
    public async Task SetCardFeesByUrlAsync(string source, IReadOnlyDictionary<string, (decimal ShippingFee, string? PickupLocation)> feesByUrl, CancellationToken cancellationToken)
    {
        string[] urls = [.. feesByUrl.Keys];
        List<PostingEntity> postings = await db.Postings
            .Where(p => p.Source == source && urls.Contains(p.Url))
            .ToListAsync(cancellationToken);

        foreach (PostingEntity posting in postings)
        {
            (posting.ShippingFee, posting.PickupLocation) = feesByUrl[posting.Url];
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAttributes(int postingId, List<PostingAttributeEntity> existing, IReadOnlyDictionary<string, string> attributes, RunEntity run)
    {
        foreach ((string rawName, string rawValue) in attributes)
        {
            string name = rawName.Trim();
            string value = rawValue.Trim();
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            PostingAttributeEntity? current = existing.FirstOrDefault(a => a.Name == name);
            if (current is null)
            {
                var added = new PostingAttributeEntity
                {
                    PostingId = postingId,
                    Name = name,
                    Value = value,
                    ObservedRunId = run.Id,
                };
                db.PostingAttributes.Add(added);
                existing.Add(added);
            }
            else
            {
                current.Value = value;
                current.ObservedRunId = run.Id;
            }
        }
    }

    /// <summary>The canonical URL of every posting the ledger holds for <paramref name="source"/>,
    /// which is what a walk compares a search page's links against to tell a listing it has seen from one
    /// it has not.</summary>
    public async Task<HashSet<string>> KnownUrlsAsync(string source, CancellationToken cancellationToken) =>
        [.. await db.Postings.Where(p => p.Source == source).Select(p => p.Url).ToListAsync(cancellationToken)];

    /// <summary>How many postings the ledger holds for each source and model, keyed by the source name
    /// and the model as the vehicle row stores it (the bare model, without its make), so the start of a
    /// walk can say how much of each pair the ledger already covers.</summary>
    public async Task<Dictionary<(string Source, string Model), int>> KnownPostingCountsAsync(CancellationToken cancellationToken) =>
        (await db.Vehicles
            .SelectMany(v => v.Postings, (v, p) => new { p.Source, v.Model })
            .GroupBy(r => r)
            .Select(g => new { g.Key.Source, g.Key.Model, Count = g.Count() })
            .ToListAsync(cancellationToken))
        .ToDictionary(r => (r.Source, r.Model), r => r.Count);

    /// <summary>Records that the run saw the postings at <paramref name="source"/> and each URL of
    /// <paramref name="cardPricesByUrl"/> on a search page, without opening their detail pages, in one
    /// save so either every touch lands or none does. Each touch stamps the posting's LastSeen with the
    /// run's own StartedAt, so the diff sees a listing still up and finds it "gone" only when a run never
    /// touches it, and appends a price observation when the card price is known and differs from the
    /// latest, so the diff reports a drop from the card alone. A card price below
    /// <see cref="PlaceholderPrice.Floor"/> is a misread (a monthly payment, say), not an asking price, so
    /// it is treated as unknown. A posting whose latest observation already carries the run's StartedAt (a
    /// detail visit this run recorded it) gets no card observation, since the detail page's price is the
    /// firmer reading and two observations at one instant would leave "latest" to row order. Everything
    /// else about a posting stays as the last detail visit left it: its dealer, its shipping fee (unless
    /// <see cref="SetCardFeesByUrlAsync"/> replaces it from the card), its pickup option, its fee
    /// posture, and its vehicle row. A URL that matches no posting writes nothing and is not counted in
    /// <see cref="TouchOutcome.Found"/>.</summary>
    public async Task<TouchOutcome> TouchAsync(string source, IReadOnlyDictionary<string, decimal?> cardPricesByUrl, RunEntity run, CancellationToken cancellationToken)
    {
        string[] urls = [.. cardPricesByUrl.Keys];
        List<PostingEntity> postings = await db.Postings
            .Include(p => p.PriceObservations)
            .Where(p => p.Source == source && urls.Contains(p.Url))
            .ToListAsync(cancellationToken);

        int priceChanges = 0;
        foreach (PostingEntity posting in postings)
        {
            posting.LastSeen = run.StartedAt;
            PriceObservationEntity? latest = posting.PriceObservations
                .OrderByDescending(o => o.ObservedAt)
                .FirstOrDefault();
            if (cardPricesByUrl[posting.Url] is decimal price
                && !PlaceholderPrice.IsBelowFloor(price)
                && latest?.Price != price
                && latest?.ObservedAt != run.StartedAt)
            {
                posting.PriceObservations.Add(new PriceObservationEntity
                {
                    PostingId = posting.Id,
                    Price = price,
                    ObservedAt = run.StartedAt,
                });
                priceChanges++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new TouchOutcome(postings.Count, priceChanges);
    }

    /// <summary>Marks every posting the ledger holds at <paramref name="source"/> and
    /// <paramref name="url"/> as sold for this run, because its detail page said the car has sold: stamps
    /// <see cref="PostingEntity.SoldSeenAt"/> with the run's own StartedAt. Nothing else about the posting
    /// changes: not <see cref="PostingEntity.LastSeen"/>, since the run did not see the car for sale, so
    /// the diff reports it gone with the reason "sold", and never a price observation, since a sold page
    /// carries other cars' prices. Returns how many postings were marked, zero for a link the ledger
    /// does not hold (a sold page for a car this walk never saved has nothing to mark).</summary>
    public async Task<int> MarkSoldAsync(string source, string url, RunEntity run, CancellationToken cancellationToken)
    {
        List<PostingEntity> postings = await db.Postings
            .Where(p => p.Source == source && p.Url == url)
            .ToListAsync(cancellationToken);

        foreach (PostingEntity posting in postings)
        {
            posting.SoldSeenAt = run.StartedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return postings.Count;
    }

    /// <summary>The dealer a sighting whose source named none (<see cref="ListingCandidate.DealerNameIsFallback"/>,
    /// carvana's "Carvana", carmax's "CarMax") links its posting to, or null to leave the posting's link as it is. The
    /// VIN history the ledger already stores may name the hub the car sits in for this posting's
    /// window (see <see cref="CarvanaDealers.HubNameFromHistory"/>): a posting with no dealer, or one
    /// on a "Carvana" row, moves to that hub. Otherwise a posting with no dealer gets the fallback
    /// dealer, and a posting that already has one keeps it: a fallback name says the source named no
    /// dealer, so it must not replace a link to a real one, nor mint a dealer row a posting will
    /// never use.</summary>
    private async ValueTask<DealerEntity?> ResolveFallbackDealerAsync(ListingCandidate candidate, PostingEntity? posting, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // The hub lookup and the move off a "Carvana" row are Carvana's: another site's fallback (carmax's
        // "CarMax") must not be linked to a Carvana hub a VIN's earlier stay happened to have.
        bool carvanaFallback = CarvanaDealers.IsChainOrHubName(candidate.DealerName);
        bool mayMove = posting?.Dealer is null || (carvanaFallback && CarvanaDealers.IsChain(posting.Dealer));
        if (!mayMove)
        {
            return null;
        }

        if (!carvanaFallback)
        {
            return await FindOrCreateDealerAsync(candidate.DealerName, candidate.DealerLocation, cancellationToken);
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
