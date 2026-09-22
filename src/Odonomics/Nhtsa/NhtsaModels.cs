namespace Odonomics.Nhtsa;

public sealed record VinDecodeResult(
    string Vin,
    int? Year,
    string? Make,
    string? Model,
    string? Trim,
    string? BodyClass,
    string? EngineCylinders,
    string? ErrorText);

/// <summary>
/// One NHTSA recall campaign for a make/model/model-year. The free recallsByVehicle endpoint has
/// no per-VIN remedy-completed status (that needs a paid VIN history service), so v0 treats every
/// campaign this endpoint returns as "open" rather than guessing at completion; see the brief's
/// instruction to degrade gracefully on an uncertain detail rather than guess an endpoint shape.
/// </summary>
public sealed record RecallEntry(
    string CampaignNumber,
    string Component,
    string Summary,
    string Consequence,
    string Remedy,
    string ReportReceivedDate)
{
    /// <summary>True once NHTSA's free-text <see cref="Remedy"/> field actually describes a fix.
    /// The free recallsByVehicle endpoint has no boolean remedy-status field; a campaign with no
    /// remedy yet either leaves the field blank or fills it with wording to that effect (e.g.
    /// "Remedy is not yet available. Please check back for updates."), so that free text is the
    /// only signal v0 has for whether a fix exists.</summary>
    public bool RemedyAvailable =>
        !string.IsNullOrWhiteSpace(Remedy) && !Remedy.Contains("not yet available", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The recalls for one make/model/model-year, or the reason NHTSA's recallsByVehicle
/// endpoint could not be fetched after a retry (see NhtsaClient), never a raw JSON exception. Only
/// one of the two is meaningful: <see cref="Entries"/> is empty when
/// <see cref="CouldNotFetchReason"/> is set.</summary>
public sealed record RecallsResult(IReadOnlyList<RecallEntry> Entries, string? CouldNotFetchReason);

/// <summary>The complaint count for one make/model/model-year, or the reason NHTSA's
/// complaintsByVehicle endpoint could not be fetched after a retry (see NhtsaClient). Only one of
/// the two is meaningful: <see cref="Count"/> is 0 when <see cref="CouldNotFetchReason"/> is
/// set.</summary>
public sealed record ComplaintsResult(int Count, string? CouldNotFetchReason);

/// <summary>NHTSA's five-star safety ratings for one year/make/model. A rating is null when NHTSA
/// has not tested that category ("Not Rated" in the raw response) rather than 0, so a caller never
/// mistakes "untested" for "tested and failed." <see cref="ErrorText"/> is set, never thrown, when
/// NHTSA has no vehicle or no rating detail on file at all for this year/make/model: a permanent,
/// legitimate absence that the seven-day refresh rule alone is fine to re-check on.
/// <see cref="CouldNotFetchReason"/> is set instead, also never thrown, when the SafetyRatings
/// endpoint itself could not be fetched after a retry (see NhtsaClient): a transient failure that
/// should be retried on the very next research run rather than waiting out that rule. At most one
/// of <see cref="ErrorText"/> and <see cref="CouldNotFetchReason"/> is ever set.</summary>
public sealed record SafetyRatingsResult(
    int? OverallRating,
    int? FrontRating,
    int? SideRating,
    int? RolloverRating,
    string? VehicleDescription,
    string? ErrorText,
    string? CouldNotFetchReason);
