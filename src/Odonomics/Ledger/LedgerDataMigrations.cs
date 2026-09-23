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
    /// leaves one dealer row named exactly "Carvana". Two steps, in this order. First, a located
    /// "Carvana" row (the older walk stored the pickup city the page printed as the dealer's
    /// location) is folded into the bare one: its postings are re-pointed, the row is removed, and
    /// the bare row is created if it did not exist. Second, every carvana posting on the bare row is
    /// re-linked to the hub the vehicle's stored Marketcheck history names for the posting's own
    /// window (see <see cref="CarvanaDealers.HubNameFromHistory"/>), reusing that hub's dealer row or
    /// creating it with no location; a posting whose history names no hub stays on the bare row.
    /// The bare row's grade is cleared whenever this runs, since a grade it holds came from the old
    /// grader accepting whichever hub's card matched, and the dealer grade run then records its own
    /// verdict on the row afresh.</summary>
    private static void ResolveCarvanaHubDealers(OdonomicsDbContext db)
    {
        List<DealerEntity> chainRows = [.. db.Dealers.Where(d => d.NormalizedName == CarvanaDealers.ChainNormalizedName)];
        if (chainRows.Count == 0)
        {
            return;
        }

        DealerEntity bare = chainRows.FirstOrDefault(CarvanaDealers.IsBareChain)
            ?? db.Dealers.Add(new DealerEntity
            {
                Name = WalkSites.CarvanaDealerName,
                NormalizedName = CarvanaDealers.ChainNormalizedName,
                NormalizedLocation = "",
            }).Entity;
        HashSet<int> locatedIds = [.. chainRows.Where(d => !CarvanaDealers.IsBareChain(d)).Select(d => d.Id)];

        int[] chainIds = [.. chainRows.Select(d => d.Id)];
        List<PostingEntity> chainPostings = [.. db.Postings.Where(p => p.DealerId != null && chainIds.Contains(p.DealerId.Value))];
        foreach (PostingEntity posting in chainPostings.Where(p => p.DealerId is int id && locatedIds.Contains(id)))
        {
            posting.Dealer = bare;
        }

        db.Dealers.RemoveRange(chainRows.Where(d => locatedIds.Contains(d.Id)));
        bare.Grade = null;
        bare.GradeReason = null;
        bare.GradeCheckedAt = null;

        List<PostingEntity> carvanaPostings = [.. chainPostings.Where(p => p.Source == WalkSites.Carvana.Name)];
        string[] vins = [.. carvanaPostings.Select(p => p.VehicleVin).Distinct()];
        Dictionary<string, string?> historyByVin = db.VinRecords
            .Where(r => vins.Contains(r.Vin))
            .Select(r => new { r.Vin, r.HistoryRawJson })
            .ToDictionary(r => r.Vin, r => r.HistoryRawJson);

        Dictionary<string, DealerEntity> hubsByNormalizedName = [];
        foreach (PostingEntity posting in carvanaPostings)
        {
            string? hubName = CarvanaDealers.HubNameFromHistory(
                historyByVin.GetValueOrDefault(posting.VehicleVin), posting.FirstSeen, posting.LastSeen);
            if (hubName is null)
            {
                posting.Dealer = bare;
                continue;
            }

            string normalizedHubName = DealerNormalizer.Normalize(hubName);
            if (!hubsByNormalizedName.TryGetValue(normalizedHubName, out DealerEntity? hub))
            {
                hub = db.Dealers.FirstOrDefault(d => d.NormalizedName == normalizedHubName && d.NormalizedLocation == "")
                    ?? db.Dealers.Add(new DealerEntity { Name = hubName, NormalizedName = normalizedHubName, NormalizedLocation = "" }).Entity;
                hubsByNormalizedName[normalizedHubName] = hub;
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
