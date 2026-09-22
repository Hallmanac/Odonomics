using System.Text.Json;
using Odonomics.Domain;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;

namespace Odonomics.Ledger;

/// <summary>The NHTSA decode/recalls/complaints/safety-ratings and Marketcheck VIN-history lookup
/// for one vehicle, whether just fetched or read back from the cached <see cref="VinRecordEntity"/>.
/// The same shape either way, so a caller (ShowRenderer, the red-flags evaluator) never needs to
/// know which path produced it.</summary>
public sealed record VinResearchResult(
    VinDecodeResult Decode,
    IReadOnlyList<RecallEntry> Recalls,
    int ComplaintCount,
    SafetyRatingsResult Safety,
    VinHistoryResult History);

/// <summary>Whether a vehicle has been researched (the safety-ratings/VIN-history lookup, not just
/// the NHTSA decode `odo show` always ran) and, if so, whether any red flag exists. Purely a display
/// concern for `odo rank`'s research column; it never feeds the ranking math.</summary>
public sealed record ResearchStatus(bool Researched, DateTimeOffset? ResearchedAt, bool HasRedFlag);

/// <summary>Fetches and caches the research lookup `odo show` and `odo research` both run: NHTSA
/// decode, recalls, complaints, and safety ratings, plus the Marketcheck VIN history. Refreshed only
/// on request or when the cached record is older than <see cref="RefreshInterval"/>, per the seven-day
/// refresh rule.</summary>
public sealed class VinResearchService(NhtsaClient nhtsa, MarketcheckHistoryClient marketcheck)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

    public static bool NeedsRefresh(VinRecordEntity? record, bool refresh) =>
        refresh || record?.ResearchedAt is not DateTimeOffset researchedAt || DateTimeOffset.UtcNow - researchedAt > RefreshInterval;

    /// <summary>Fetches fresh data from NHTSA and Marketcheck and persists it onto
    /// <paramref name="vehicle"/>'s <see cref="VinRecordEntity"/>, creating one if this is the
    /// vehicle's first research. A Marketcheck fetch failure (including a missing key) never throws
    /// here: <see cref="MarketcheckHistoryClient"/> already degrades that to
    /// <see cref="VinHistoryResult.CouldNotFetchReason"/>, and the vehicle's previously cached
    /// history (if any) is left untouched rather than being overwritten with nothing. An NHTSA
    /// failure does throw, since v0 has no key to be missing there and a genuine outage is the
    /// caller's to report as this vehicle's own failed lookup.</summary>
    public async Task<VinResearchResult> RefreshAsync(OdonomicsDbContext db, VehicleEntity vehicle, CancellationToken cancellationToken)
    {
        VinDecodeResult decode = await nhtsa.DecodeVinAsync(vehicle.Vin, cancellationToken);
        IReadOnlyList<RecallEntry> recalls = await nhtsa.GetRecallsAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken);
        int complaintCount = await nhtsa.GetComplaintCountAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken);
        SafetyRatingsResult safety = await nhtsa.GetSafetyRatingsAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken);
        VinHistoryResult history = await marketcheck.GetHistoryAsync(vehicle.Vin, cancellationToken);

        VinRecordEntity? record = vehicle.VinRecord;
        if (record is null)
        {
            record = new VinRecordEntity { Vin = vehicle.Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "" };
            db.VinRecords.Add(record);
        }

        record.DecodedAt = DateTimeOffset.UtcNow;
        record.DecodeRawJson = JsonSerializer.Serialize(decode);
        record.OpenRecallCount = recalls.Count;
        record.RecallsRawJson = JsonSerializer.Serialize(recalls);
        record.ComplaintCount = complaintCount;
        record.ResearchedAt = DateTimeOffset.UtcNow;
        record.SafetyOverallRating = safety.OverallRating;
        record.SafetyFrontRating = safety.FrontRating;
        record.SafetySideRating = safety.SideRating;
        record.SafetyRolloverRating = safety.RolloverRating;
        record.SafetyRawJson = JsonSerializer.Serialize(safety);

        if (history.CouldNotFetchReason is null)
        {
            record.HistoryRawJson = JsonSerializer.Serialize(history.PriorListings);
            record.CurrentListingDaysOnMarket = history.CurrentListingDaysOnMarket;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new VinResearchResult(decode, recalls, complaintCount, safety, history);
    }

    /// <summary>Rebuilds a <see cref="VinResearchResult"/> from a cached record's raw JSON, without
    /// any network call.</summary>
    public static VinResearchResult FromCached(VinRecordEntity record)
    {
        VinDecodeResult decode = string.IsNullOrEmpty(record.DecodeRawJson)
            ? new VinDecodeResult(record.Vin, null, null, null, null, null, null, null)
            : JsonSerializer.Deserialize<VinDecodeResult>(record.DecodeRawJson)!;
        IReadOnlyList<RecallEntry> recalls = record.RecallsRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<RecallEntry>>(record.RecallsRawJson) ?? [];
        SafetyRatingsResult safety = record.SafetyRawJson is null
            ? new SafetyRatingsResult(null, null, null, null, null, "no NHTSA safety rating fetched yet")
            : JsonSerializer.Deserialize<SafetyRatingsResult>(record.SafetyRawJson)!;
        IReadOnlyList<VinHistoryListing> priorListings = record.HistoryRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<VinHistoryListing>>(record.HistoryRawJson) ?? [];
        VinHistoryResult history = new(
            priorListings,
            record.CurrentListingDaysOnMarket,
            record.HistoryRawJson is null ? "no Marketcheck VIN history fetched yet" : null);

        return new VinResearchResult(decode, recalls, record.ComplaintCount, safety, history);
    }

    /// <summary>The red flags for a cached record against a vehicle's current asking price, without
    /// re-fetching anything. Used by `odo rank`'s research column, which never makes a network call.
    /// Reads <see cref="VinRecordEntity.OpenRecallCount"/> directly rather than going through
    /// <see cref="FromCached"/>'s parsed recall list, since the recall count is the only part of
    /// that list red flags actually needs and it's the field the entity treats as authoritative.</summary>
    public static IReadOnlyList<string> RedFlagsForCached(VinRecordEntity record, decimal? currentPrice)
    {
        IReadOnlyList<VinHistoryListing> priorListings = record.HistoryRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<VinHistoryListing>>(record.HistoryRawJson) ?? [];

        return RedFlagsEvaluator.Evaluate(
            record.OpenRecallCount,
            record.SafetyOverallRating,
            [.. priorListings.Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.Price, l.Mileage))],
            currentPrice);
    }

    public static IReadOnlyList<string> RedFlags(VinResearchResult research, decimal? currentPrice) =>
        RedFlagsEvaluator.Evaluate(
            research.Recalls.Count,
            research.Safety.OverallRating,
            [.. research.History.PriorListings.Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.Price, l.Mileage))],
            currentPrice);
}
