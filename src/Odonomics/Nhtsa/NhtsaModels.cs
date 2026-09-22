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
    string ReportReceivedDate);

/// <summary>NHTSA's five-star safety ratings for one year/make/model. A rating is null when NHTSA
/// has not tested that category ("Not Rated" in the raw response) rather than 0, so a caller never
/// mistakes "untested" for "tested and failed." <see cref="ErrorText"/> is set instead of throwing
/// when NHTSA has no vehicle on file at all for this year/make/model; a category NHTSA simply never
/// rated on an otherwise-found vehicle is not an error, just a null star count.</summary>
public sealed record SafetyRatingsResult(
    int? OverallRating,
    int? FrontRating,
    int? SideRating,
    int? RolloverRating,
    string? VehicleDescription,
    string? ErrorText);
