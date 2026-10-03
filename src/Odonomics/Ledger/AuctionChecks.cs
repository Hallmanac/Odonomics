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
}
