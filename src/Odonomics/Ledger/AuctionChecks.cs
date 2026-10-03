using System.Globalization;
using Odonomics.Auctions;
using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>The rules for a vehicle's stored salvage-auction lookup (see <see cref="AuctionCheckEntity"/>):
/// when `odo title check` looks it up again, how a lookup result is stored, and the red flag a found
/// sale raises. It needs no research fetch, so `odo rank` can show the flag for a vehicle nobody has
/// researched.</summary>
public static class AuctionChecks
{
    public static readonly TimeSpan RecheckInterval = TimeSpan.FromDays(7);

    /// <summary>A vehicle is looked up when it never was, when its last lookup could not read the
    /// archives, or when the last lookup is more than seven days old.</summary>
    public static bool NeedsCheck(AuctionCheckEntity? check, DateTimeOffset now) =>
        check is null
        || check.Outcome == AuctionCheckOutcome.CouldNotRead
        || now - check.CheckedAt > RecheckInterval;

    /// <summary>Stores <paramref name="result"/> on <paramref name="vehicle"/>, replacing whatever the
    /// last lookup stored, and stamps it with <paramref name="checkedAt"/>.</summary>
    public static AuctionCheckEntity Apply(VehicleEntity vehicle, AuctionLookupResult result, DateTimeOffset checkedAt)
    {
        AuctionCheckEntity check = vehicle.AuctionCheck ?? new AuctionCheckEntity { Vin = vehicle.Vin, Outcome = result.Outcome, CheckedAt = checkedAt };
        AuctionRecord? record = result.Record;
        check.Outcome = result.Outcome;
        check.CheckedAt = checkedAt;
        check.CouldNotReadReason = result.Reason;
        check.Auction = record?.Auction;
        check.LotNumber = record?.LotNumber;
        check.SaleDate = record?.SaleDate;
        check.SaleDocument = record?.SaleDocument;
        check.PrimaryDamage = record?.PrimaryDamage;
        check.SecondaryDamage = record?.SecondaryDamage;
        check.Acv = record?.Acv;
        check.RepairEstimate = record?.RepairEstimate;
        check.Odometer = record?.Odometer;
        check.SourceUrl = record?.SourceUrl;
        vehicle.AuctionCheck = check;
        return check;
    }

    /// <summary>The salvage-auction flag for a vehicle whose stored lookup found a sale, as a list of
    /// zero or one so a caller can append it to the flags it already has.</summary>
    public static IReadOnlyList<RedFlag> RedFlags(AuctionCheckEntity? check) =>
        check is { Outcome: AuctionCheckOutcome.Found }
            ? [RedFlagsEvaluator.SalvageAuction(check.SaleDocument, check.PrimaryDamage, check.SecondaryDamage, check.SaleDate)]
            : [];

    /// <summary>What `odo rank` names when it excludes a vehicle whose stored lookup found a sale: the
    /// auction, the sale date, and the sale document, in that order, such as "Copart 2026-07-16, Salvage
    /// certificate (CA)". Any part the record did not print is left out. Null for a lookup that found
    /// nothing, could not read, or never ran, none of which excludes a vehicle. Any found sale counts
    /// whatever its sale document, since Copart and IAA are insurance-salvage auctions even when a lot
    /// sells on a clean title.</summary>
    public static string? ExclusionSale(AuctionCheckEntity? check)
    {
        if (check is not { Outcome: AuctionCheckOutcome.Found })
        {
            return null;
        }

        string auctionAndDate = string.Join(" ", PrintedParts(check.Auction, check.SaleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        string sale = string.Join(", ", PrintedParts(auctionAndDate, check.SaleDocument));
        return sale.Length == 0
            ? "no sale details printed"
            : sale;
    }

    private static IEnumerable<string> PrintedParts(params string?[] parts) =>
        parts.OfType<string>().Select(part => part.Trim()).Where(part => part.Length > 0);
}
