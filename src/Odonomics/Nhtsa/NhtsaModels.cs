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
