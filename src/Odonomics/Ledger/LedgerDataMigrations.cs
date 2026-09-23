using Microsoft.EntityFrameworkCore;
using Odonomics.Walk;

namespace Odonomics.Ledger;

/// <summary>One-time data migrations against ledger rows already on disk, as opposed to the
/// schema migrations under <see cref="Ledger.Migrations"/>. <see cref="LedgerFactory.Open"/> calls
/// <see cref="ApplyAll"/> on every startup; each migration records its own name in
/// <see cref="OdonomicsDbContext.LedgerMigrations"/> the moment it commits, so a later startup
/// skips it instead of re-scanning the whole ledger every time the CLI runs.</summary>
public static class LedgerDataMigrations
{
    private const string CanonicalizeWalkedPostingUrlsName = "CanonicalizeWalkedPostingUrls";
    private const string StampCarvanaFallbackDealerName = "StampCarvanaFallbackDealer";
    private const string ResolveCarvanaHubDealersName = "ResolveCarvanaHubDealers";

    private static readonly string[] WalkedSources = [WalkSites.CarsCom.Name, WalkSites.Carvana.Name];

    public static void ApplyAll(OdonomicsDbContext db)
    {
        ApplyOnce(db, CanonicalizeWalkedPostingUrlsName, CanonicalizeWalkedPostingUrls);
        ApplyOnce(db, StampCarvanaFallbackDealerName, StampCarvanaFallbackDealer);
        ApplyOnce(db, ResolveCarvanaHubDealersName, ResolveCarvanaHubDealers);
    }

    private static void ApplyOnce(OdonomicsDbContext db, string name, Action<OdonomicsDbContext> migration)
    {
        if (db.LedgerMigrations.Any(m => m.Name == name))
        {
            return;
        }

        migration(db);
        db.LedgerMigrations.Add(new LedgerMigrationEntity { Name = name, AppliedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    /// <summary>Rewrites every stored cars.com or carvana posting URL to its canonical form (see
    /// <see cref="WalkSites.CanonicalDetailUrl"/>), the same transform the walk itself now applies
    /// before ever upserting a posting for either walk target. Rows written before that fix still
    /// carry a session-id-bearing URL, so two postings for the same VIN can collapse to the same
    /// canonical URL; those are merged into one row, keeping the earliest FirstSeen, the latest
    /// LastSeen, whichever row already had a dealer linked, and every price observation from both
    /// rows. A stored URL that isn't a valid absolute URI is left untouched rather than aborting
    /// the whole migration: this runs on every CLI startup until it commits, so one bad historical
    /// row would otherwise block every `odo` command permanently.</summary>
    private static void CanonicalizeWalkedPostingUrls(OdonomicsDbContext db)
    {
        List<PostingEntity> walkedPostings = [.. db.Postings
            .Include(p => p.PriceObservations)
            .Where(p => WalkedSources.Contains(p.Source))];

        IEnumerable<IGrouping<(string Vin, string CanonicalUrl), PostingEntity>> groups = walkedPostings
            .Select(p => (Posting: p, CanonicalUrl: TryCanonicalize(p.Url)))
            .Where(t => t.CanonicalUrl is not null)
            .GroupBy(t => (Vin: t.Posting.VehicleVin, CanonicalUrl: t.CanonicalUrl!), t => t.Posting);

        foreach (IGrouping<(string Vin, string CanonicalUrl), PostingEntity> group in groups)
        {
            List<PostingEntity> rows = [.. group.OrderBy(p => p.FirstSeen).ThenBy(p => p.Id)];
            PostingEntity survivor = rows[0];
            survivor.Url = group.Key.CanonicalUrl;
            survivor.LastSeen = rows.Max(p => p.LastSeen);

            foreach (PostingEntity duplicate in rows.Skip(1))
            {
                survivor.DealerId ??= duplicate.DealerId;
                foreach (PriceObservationEntity observation in duplicate.PriceObservations)
                {
                    observation.PostingId = survivor.Id;
                }

                db.Postings.Remove(duplicate);
            }
        }
    }

    /// <summary>Links every carvana posting with no dealer to the "Carvana" dealer, the same one the
    /// walk now stamps when a detail page names no hub (see <see cref="WalkSite.ResolveDealer"/>),
    /// so a ledger walked before that change shows a dealer without a fresh walk. A posting that
    /// already has a dealer, such as a named hub, is left alone. The dealer is the one with no
    /// location, which is also the only one the walk stamps as the fallback (it drops any location
    /// the page printed); an existing "Carvana" dealer that does have a location is a different
    /// dealer row and is not reused here (<see cref="ResolveCarvanaHubDealers"/> folds it
    /// away).</summary>
    private static void StampCarvanaFallbackDealer(OdonomicsDbContext db)
    {
        List<PostingEntity> undealered = [.. db.Postings.Where(p => p.Source == WalkSites.Carvana.Name && p.DealerId == null)];
        if (undealered.Count == 0)
        {
            return;
        }

        string normalizedName = DealerNormalizer.Normalize(WalkSites.CarvanaDealerName);
        DealerEntity dealer = db.Dealers.FirstOrDefault(d => d.NormalizedName == normalizedName && d.NormalizedLocation == "")
            ?? new DealerEntity
            {
                Name = WalkSites.CarvanaDealerName,
                NormalizedName = normalizedName,
                NormalizedLocation = "",
            };

        foreach (PostingEntity posting in undealered)
        {
            posting.Dealer = dealer;
        }
    }

    /// <summary>Gives every carvana posting the most specific Carvana dealer the ledger knows, and
    /// leaves one location-less dealer row per Carvana name. Two steps, in this order. First, the
    /// Carvana rows are folded by name: the older walk stored the pickup city the page printed as the
    /// dealer's location, on "Carvana" and on a named hub such as "Carvana Winder" alike, so a name
    /// can have several rows. The location-less row survives (or, when every row of a name has a
    /// location, the oldest one, stripped of it), the others have their postings re-pointed to it
    /// and are removed. Second, every carvana posting on the bare "Carvana" row is re-linked to the
    /// hub the vehicle's stored Marketcheck history names for the posting's own window (see
    /// <see cref="CarvanaDealers.HubNameFromHistory"/>), reusing that hub's dealer row or creating it
    /// with no location; a posting whose history names no hub stays on the bare row. The bare row's
    /// grade is cleared whenever this runs, since a grade it holds came from the old grader accepting
    /// whichever hub's card matched, and the dealer grade run then records its own verdict on the
    /// row afresh. A hub row keeps the grade it earned under its own name.</summary>
    private static void ResolveCarvanaHubDealers(OdonomicsDbContext db)
    {
        List<DealerEntity> chainRows = [.. db.Dealers.Where(d => d.NormalizedName == CarvanaDealers.ChainNormalizedName
            || d.NormalizedName.StartsWith(CarvanaDealers.ChainNormalizedName + " "))];
        if (chainRows.Count == 0)
        {
            return;
        }

        Dictionary<string, DealerEntity> dealersByNormalizedName = [];
        Dictionary<int, DealerEntity> survivorById = [];
        List<DealerEntity> duplicates = [];
        foreach (IGrouping<string, DealerEntity> sameName in chainRows.GroupBy(d => d.NormalizedName))
        {
            List<DealerEntity> rows = [.. sameName.OrderBy(d => d.NormalizedLocation != "").ThenBy(d => d.Id)];
            DealerEntity survivor = rows[0];
            survivor.Location = null;
            survivor.NormalizedLocation = "";
            dealersByNormalizedName[sameName.Key] = survivor;
            foreach (DealerEntity row in rows)
            {
                survivorById[row.Id] = survivor;
            }

            duplicates.AddRange(rows.Skip(1));
        }

        DealerEntity? bare = dealersByNormalizedName.GetValueOrDefault(CarvanaDealers.ChainNormalizedName);
        if (bare is not null)
        {
            bare.Grade = null;
            bare.GradeReason = null;
            bare.GradeCheckedAt = null;
        }

        int[] chainIds = [.. survivorById.Keys];
        List<PostingEntity> chainPostings = [.. db.Postings.Where(p => p.DealerId != null && chainIds.Contains(p.DealerId.Value))];
        List<PostingEntity> barePostings = [];
        foreach (PostingEntity posting in chainPostings)
        {
            if (posting.DealerId is not int dealerId)
            {
                continue;
            }

            DealerEntity survivor = survivorById[dealerId];
            posting.Dealer = survivor;
            if (survivor == bare && posting.Source == WalkSites.Carvana.Name)
            {
                barePostings.Add(posting);
            }
        }

        // Removed only after every posting is re-pointed: deleting a tracked dealer nulls the link of
        // any posting already tracked against it.
        db.Dealers.RemoveRange(duplicates);

        string[] vins = [.. barePostings.Select(p => p.VehicleVin).Distinct()];
        Dictionary<string, string?> historyByVin = db.VinRecords
            .Where(r => vins.Contains(r.Vin))
            .Select(r => new { r.Vin, r.HistoryRawJson })
            .ToDictionary(r => r.Vin, r => r.HistoryRawJson);

        foreach (PostingEntity posting in barePostings)
        {
            string? hubName = CarvanaDealers.HubNameFromHistory(
                historyByVin.GetValueOrDefault(posting.VehicleVin), posting.FirstSeen, posting.LastSeen);
            if (hubName is null)
            {
                continue;
            }

            // Every existing hub row was loaded and folded above, so a name missing here has no row yet.
            string normalizedHubName = DealerNormalizer.Normalize(hubName);
            if (!dealersByNormalizedName.TryGetValue(normalizedHubName, out DealerEntity? hub))
            {
                hub = db.Dealers.Add(new DealerEntity { Name = hubName, NormalizedName = normalizedHubName, NormalizedLocation = "" }).Entity;
                dealersByNormalizedName[normalizedHubName] = hub;
            }

            posting.Dealer = hub;
        }
    }

    private static string? TryCanonicalize(string url)
    {
        try
        {
            return WalkSites.CanonicalDetailUrl(url);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
