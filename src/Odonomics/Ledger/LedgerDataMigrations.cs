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

    private static readonly string[] WalkedSources = [WalkSites.CarsCom.Name, WalkSites.Carvana.Name];

    public static void ApplyAll(OdonomicsDbContext db)
    {
        ApplyOnce(db, CanonicalizeWalkedPostingUrlsName, CanonicalizeWalkedPostingUrls);
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
