using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;

namespace Odonomics.Ledger;

/// <summary>The NHTSA decode/recalls/complaints/safety-ratings and Marketcheck VIN-history lookup
/// for one vehicle, whether just fetched or read back from the cached <see cref="VinRecordEntity"/>.
/// The same shape either way, so a caller (ShowRenderer, the red-flags evaluator) never needs to
/// know which path produced it. <see cref="Recalls"/>, <see cref="Complaints"/>, and
/// <see cref="Safety"/> each carry their own could-not-fetch reason (see NhtsaClient's
/// retry-then-degrade behavior), independent of one another and of <see cref="Decode"/> and
/// <see cref="History"/>, so one bad NHTSA answer never hides the pieces that did come back.</summary>
public sealed record VinResearchResult(
    VinDecodeResult Decode,
    RecallsResult Recalls,
    ComplaintsResult Complaints,
    SafetyRatingsResult Safety,
    VinHistoryResult History);

/// <summary>Whether a vehicle has been researched (the safety-ratings/VIN-history lookup, not just
/// the NHTSA decode `odo show` always ran), whether any red flag exists, and how many open NHTSA
/// recalls it carries. <see cref="RecallsKnown"/> is false when the recalls piece has never once
/// succeeded (e.g. every attempt hit NHTSA's HTML-error-page failure mode), so <see cref="RecallCount"/>'s
/// default 0 is never mistaken for a confirmed "no open recalls" by a caller. Purely a display concern
/// for `odo rank`'s research and recalls columns; none of it ever feeds the ranking math.</summary>
public sealed record ResearchStatus(bool Researched, DateTimeOffset? ResearchedAt, bool HasRedFlag, bool RecallsKnown, int RecallCount);

/// <summary>Fetches and caches the research lookup `odo show` and `odo research` both run: NHTSA
/// decode, recalls, complaints, and safety ratings, plus the Marketcheck VIN history. Refreshed on
/// request, when the cached record is older than <see cref="RefreshInterval"/> (the seven-day refresh
/// rule), when the Marketcheck VIN history was never successfully fetched (a missing key or a failed
/// call leaves <see cref="VinRecordEntity.HistoryRawJson"/> null), or when recalls, complaints, or
/// safety ratings previously could not be fetched, so a batch retries only the pieces that failed
/// last time rather than waiting out the seven-day window or re-fetching everything.</summary>
public sealed class VinResearchService(NhtsaClient nhtsa, MarketcheckHistoryClient marketcheck)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

    public static bool NeedsRefresh(VinRecordEntity? record, bool refresh) =>
        refresh
        || record?.ResearchedAt is not DateTimeOffset researchedAt
        || record.HistoryRawJson is null
        || record.RecallsCouldNotFetchReason is not null
        || record.ComplaintsCouldNotFetchReason is not null
        || record.SafetyCouldNotFetchReason is not null
        || DateTimeOffset.UtcNow - researchedAt > RefreshInterval;

    /// <summary>Fetches fresh data from NHTSA and Marketcheck and persists it onto
    /// <paramref name="vehicle"/>'s <see cref="VinRecordEntity"/>, creating one if this is the
    /// vehicle's first research. Decode is skipped, and the previously cached value reused, unless
    /// the batch is stale or force-refreshed; recalls, complaints, and safety ratings are each
    /// skipped (and their cached values reused) under that same condition UNLESS that specific piece
    /// previously could not be fetched, in which case it is retried regardless of staleness. A piece
    /// that fails here never throws (see NhtsaClient) and never blocks the others: each is persisted,
    /// or marked could-not-fetch, independently, and the returned result carries that same piece's
    /// last known-good cached value (not an empty one) alongside the reason, so a real red flag never
    /// silently disappears just because that round's retry failed. <see cref="VinRecordEntity.ResearchedAt"/>
    /// is stamped only when the batch was stale or forced AND at least one of recalls, complaints, or
    /// safety ratings actually succeeded, so a vehicle NHTSA could not answer at all is not recorded as
    /// researched. A Marketcheck fetch failure (including a missing
    /// key) never throws either: <see cref="MarketcheckHistoryClient"/> already degrades that to
    /// <see cref="VinHistoryResult.CouldNotFetchReason"/>, and the vehicle's previously cached
    /// history (if any) is left untouched rather than being overwritten with nothing.</summary>
    public async Task<VinResearchResult> RefreshAsync(OdonomicsDbContext db, VehicleEntity vehicle, bool refresh, CancellationToken cancellationToken)
    {
        VinRecordEntity? existing = vehicle.VinRecord;
        VinRecordEntity record = existing ?? new VinRecordEntity { Vin = vehicle.Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "" };
        VinResearchResult cached = FromCached(record);

        bool staleOrForced = refresh
            || record.ResearchedAt is not DateTimeOffset researchedAt
            || DateTimeOffset.UtcNow - researchedAt > RefreshInterval;
        bool refetchRecalls = staleOrForced || record.RecallsCouldNotFetchReason is not null;
        bool refetchComplaints = staleOrForced || record.ComplaintsCouldNotFetchReason is not null;
        bool refetchSafety = staleOrForced || record.SafetyCouldNotFetchReason is not null;

        VinDecodeResult decode = staleOrForced
            ? await nhtsa.DecodeVinAsync(vehicle.Vin, cancellationToken)
            : cached.Decode;
        RecallsResult recalls = refetchRecalls
            ? await nhtsa.GetRecallsAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken)
            : cached.Recalls;
        ComplaintsResult complaints = refetchComplaints
            ? await nhtsa.GetComplaintCountAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken)
            : cached.Complaints;
        SafetyRatingsResult safety = refetchSafety
            ? await nhtsa.GetSafetyRatingsAsync(vehicle.Make, vehicle.Model, vehicle.Year, cancellationToken)
            : cached.Safety;

        VinHistoryResult history = await marketcheck.GetHistoryAsync(vehicle.Vin, cancellationToken);

        if (existing is null)
        {
            db.VinRecords.Add(record);
        }

        if (staleOrForced)
        {
            record.DecodedAt = DateTimeOffset.UtcNow;
            record.DecodeRawJson = JsonSerializer.Serialize(decode);
        }

        if (refetchRecalls)
        {
            record.RecallsFetchedAt = DateTimeOffset.UtcNow;
            record.RecallsCouldNotFetchReason = recalls.CouldNotFetchReason;
            if (recalls.CouldNotFetchReason is null)
            {
                record.OpenRecallCount = recalls.Entries.Count;
                record.RecallsRawJson = JsonSerializer.Serialize(recalls.Entries);
            }
        }

        if (refetchComplaints)
        {
            record.ComplaintsFetchedAt = DateTimeOffset.UtcNow;
            record.ComplaintsCouldNotFetchReason = complaints.CouldNotFetchReason;
            if (complaints.CouldNotFetchReason is null)
            {
                record.ComplaintCount = complaints.Count;
            }
        }

        if (refetchSafety)
        {
            record.SafetyFetchedAt = DateTimeOffset.UtcNow;
            record.SafetyCouldNotFetchReason = safety.CouldNotFetchReason;
            if (safety.CouldNotFetchReason is null)
            {
                record.SafetyOverallRating = safety.OverallRating;
                record.SafetyFrontRating = safety.FrontRating;
                record.SafetySideRating = safety.SideRating;
                record.SafetyRolloverRating = safety.RolloverRating;
                record.SafetyRawJson = JsonSerializer.Serialize(safety);
            }
        }

        if (history.CouldNotFetchReason is null)
        {
            record.HistoryRawJson = JsonSerializer.Serialize(history.PriorListings);
            record.CurrentListingDaysOnMarket = history.CurrentListingDaysOnMarket;

            await AddMileageCorrectionNotesAsync(db, vehicle.Vin, history.PriorListings, cancellationToken);
        }

        bool anyPieceSucceeded = recalls.CouldNotFetchReason is null
            || complaints.CouldNotFetchReason is null
            || safety.CouldNotFetchReason is null;
        if (staleOrForced && anyPieceSucceeded)
        {
            record.ResearchedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        // A piece that failed this round keeps its last known-good data on the returned result too
        // (not just on the persisted record), the same overlay FromCached applies: only the
        // could-not-fetch reason reflects this round's failure, so a caller never sees a real red
        // flag vanish just because the retry that would have confirmed it again failed.
        RecallsResult recallsForCaller = recalls.CouldNotFetchReason is null
            ? recalls
            : cached.Recalls with { CouldNotFetchReason = recalls.CouldNotFetchReason };
        ComplaintsResult complaintsForCaller = complaints.CouldNotFetchReason is null
            ? complaints
            : cached.Complaints with { CouldNotFetchReason = complaints.CouldNotFetchReason };
        SafetyRatingsResult safetyForCaller = safety.CouldNotFetchReason is null
            ? safety
            : cached.Safety with { CouldNotFetchReason = safety.CouldNotFetchReason };

        return new VinResearchResult(decode, recallsForCaller, complaintsForCaller, safetyForCaller, history);
    }

    /// <summary>Rebuilds a <see cref="VinResearchResult"/> from a cached record's raw JSON, without
    /// any network call. Each piece's could-not-fetch reason is read from the entity's own field
    /// (not from the raw JSON, which reflects only the last successful fetch of that piece) and
    /// overlaid onto the cached value, so a piece that is currently failing still shows the reason
    /// even while its last known-good data remains on display.</summary>
    public static VinResearchResult FromCached(VinRecordEntity record)
    {
        VinDecodeResult decode = string.IsNullOrEmpty(record.DecodeRawJson)
            ? new VinDecodeResult(record.Vin, null, null, null, null, null, null, null)
            : JsonSerializer.Deserialize<VinDecodeResult>(record.DecodeRawJson)!;
        IReadOnlyList<RecallEntry> recallEntries = record.RecallsRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<RecallEntry>>(record.RecallsRawJson) ?? [];
        RecallsResult recalls = new(recallEntries, record.RecallsCouldNotFetchReason);
        ComplaintsResult complaints = new(record.ComplaintCount, record.ComplaintsCouldNotFetchReason);
        SafetyRatingsResult safety = record.SafetyRawJson is null
            ? new SafetyRatingsResult(null, null, null, null, null,
                record.SafetyCouldNotFetchReason is null ? "no NHTSA safety rating fetched yet" : null,
                record.SafetyCouldNotFetchReason)
            : JsonSerializer.Deserialize<SafetyRatingsResult>(record.SafetyRawJson)! with { CouldNotFetchReason = record.SafetyCouldNotFetchReason };
        IReadOnlyList<VinHistoryListing> priorListings = record.HistoryRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<VinHistoryListing>>(record.HistoryRawJson) ?? [];
        VinHistoryResult history = new(
            priorListings,
            record.CurrentListingDaysOnMarket,
            record.HistoryRawJson is null ? "no Marketcheck VIN history fetched yet" : null);

        return new VinResearchResult(decode, recalls, complaints, safety, history);
    }

    /// <summary>The red flags for a cached record against a vehicle's current asking price, without
    /// re-fetching anything. Used by `odo rank`'s research column, which never makes a network call.
    /// Reads <see cref="VinRecordEntity.RecallsRawJson"/> rather than <see cref="FromCached"/>'s full
    /// rebuild, since only the recall list (for remedy status) and the VIN history are needed
    /// here.</summary>
    public static IReadOnlyList<RedFlag> RedFlagsForCached(VinRecordEntity record, decimal? currentPrice)
    {
        IReadOnlyList<VinHistoryListing> priorListings = record.HistoryRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<VinHistoryListing>>(record.HistoryRawJson) ?? [];
        IReadOnlyList<RecallEntry> recalls = record.RecallsRawJson is null
            ? []
            : JsonSerializer.Deserialize<List<RecallEntry>>(record.RecallsRawJson) ?? [];

        return RedFlagsEvaluator.Evaluate(
            [.. recalls.Select(r => new RecallForFlagging(r.RemedyAvailable))],
            record.SafetyOverallRating,
            [.. priorListings.Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.LastSeen, l.Price, l.Mileage))],
            currentPrice).Flags;
    }

    public static IReadOnlyList<RedFlag> RedFlags(VinResearchResult research, decimal? currentPrice) =>
        RedFlagsEvaluator.Evaluate(
            [.. research.Recalls.Entries.Select(r => new RecallForFlagging(r.RemedyAvailable))],
            research.Safety.OverallRating,
            [.. research.History.PriorListings.Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.LastSeen, l.Price, l.Mileage))],
            currentPrice).Flags;

    /// <summary>Persists any mileage-correction note (see <see cref="RedFlagsEvaluator.Evaluate"/>'s
    /// <see cref="EvaluationResult.Notes"/>) that isn't already on the vehicle, the same way an
    /// operator's own `odo note` would, so a same-seller odometer correction shows up in `odo show`'s
    /// notes list instead of silently vanishing once it's excluded from the mileage-drop flag. Checks
    /// the database directly rather than <see cref="VehicleEntity.Notes"/>, since a caller of
    /// <see cref="RefreshAsync"/> is not guaranteed to have included that navigation.</summary>
    private static async Task AddMileageCorrectionNotesAsync(
        OdonomicsDbContext db, string vin, IReadOnlyList<VinHistoryListing> priorListings, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> notes = RedFlagsEvaluator.Evaluate(
            [], null,
            [.. priorListings.Select(l => new VinHistoryPoint(l.Dealer, l.FirstSeen, l.LastSeen, l.Price, l.Mileage))],
            null).Notes;
        if (notes.Count == 0)
        {
            return;
        }

        HashSet<string> existingNoteTexts = [.. await db.Notes.Where(n => n.VehicleVin == vin).Select(n => n.Text).ToListAsync(cancellationToken)];
        foreach (string noteText in notes)
        {
            if (existingNoteTexts.Add(noteText))
            {
                db.Notes.Add(new NoteEntity { VehicleVin = vin, Text = noteText, CreatedAt = DateTimeOffset.UtcNow });
            }
        }
    }
}
