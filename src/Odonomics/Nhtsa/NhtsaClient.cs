using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odonomics.Nhtsa;

/// <summary>
/// The free NHTSA endpoints v0 uses: vPIC VIN decode, recalls by make/model/year, and a
/// complaint count for the model year. No key is needed for any of the three.
/// </summary>
public sealed class NhtsaClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<VinDecodeResult> DecodeVinAsync(string vin, CancellationToken cancellationToken)
    {
        string url = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Uri.EscapeDataString(vin)}?format=json";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        DecodeEnvelope? envelope = JsonSerializer.Deserialize<DecodeEnvelope>(body, JsonOptions);
        DecodeRecord record = envelope?.Results?.FirstOrDefault()
            ?? throw new InvalidOperationException($"NHTSA decode returned no results for VIN {vin}");

        int? year = int.TryParse(record.ModelYear, out int parsedYear) ? parsedYear : null;

        return new VinDecodeResult(
            Vin: record.Vin ?? vin,
            Year: year,
            Make: NullIfBlank(record.Make),
            Model: NullIfBlank(record.Model),
            Trim: NullIfBlank(record.Trim),
            BodyClass: NullIfBlank(record.BodyClass),
            EngineCylinders: NullIfBlank(record.EngineCylinders),
            ErrorText: NullIfBlank(record.ErrorText));
    }

    public async Task<IReadOnlyList<RecallEntry>> GetRecallsAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string url = $"https://api.nhtsa.gov/recalls/recallsByVehicle?make={Uri.EscapeDataString(make)}&model={Uri.EscapeDataString(model)}&modelYear={modelYear}";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Not EnsureSuccessStatusCode(): this endpoint was found, by hand, to return HTTP 400
        // together with a perfectly valid "Count":0 body on a make/model/year with zero recalls,
        // while still returning 200 whenever at least one recall exists. The body is always the
        // real signal here; a genuinely malformed response still fails below at deserialization.
        RecallsEnvelope? envelope = JsonSerializer.Deserialize<RecallsEnvelope>(body, JsonOptions);
        IEnumerable<RecallRecord> records = envelope?.Results ?? [];

        return [.. records.Select(r => new RecallEntry(
            CampaignNumber: r.NHTSACampaignNumber ?? "",
            Component: r.Component ?? "",
            Summary: r.Summary ?? "",
            Consequence: r.Consequence ?? "",
            Remedy: r.Remedy ?? "",
            ReportReceivedDate: r.ReportReceivedDate ?? ""))];
    }

    public async Task<int> GetComplaintCountAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string url = $"https://api.nhtsa.gov/complaints/complaintsByVehicle?make={Uri.EscapeDataString(make)}&model={Uri.EscapeDataString(model)}&modelYear={modelYear}";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Not EnsureSuccessStatusCode(): found, by hand, to carry the same quirk as recallsByVehicle
        // (see GetRecallsAsync above), HTTP 400 with a perfectly valid "count":0 body for a
        // make/model/year with zero complaints, most often a brand-new model year. The body is the
        // real signal; a genuinely malformed response still fails below at deserialization.

        ComplaintsEnvelope? envelope = JsonSerializer.Deserialize<ComplaintsEnvelope>(body, JsonOptions);
        return envelope?.Count ?? 0;
    }

    /// <summary>NHTSA's SafetyRatings API is two calls: look up the VehicleId(s) on file for this
    /// year/make/model, then fetch the star ratings for one of them. A year/make/model can have
    /// several VehicleIds (one per body style/trim NHTSA tested separately); v0 takes the first, the
    /// same simplification the brief allows for an uncertain detail rather than guessing which body
    /// style matches this specific VIN.</summary>
    public async Task<SafetyRatingsResult> GetSafetyRatingsAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string lookupUrl = $"https://api.nhtsa.gov/SafetyRatings/modelyear/{modelYear}/make/{Uri.EscapeDataString(make)}/model/{Uri.EscapeDataString(model)}";
        using HttpResponseMessage lookupResponse = await http.GetAsync(lookupUrl, cancellationToken);
        lookupResponse.EnsureSuccessStatusCode();
        string lookupBody = await lookupResponse.Content.ReadAsStringAsync(cancellationToken);

        SafetyRatingsLookupEnvelope? lookup = JsonSerializer.Deserialize<SafetyRatingsLookupEnvelope>(lookupBody, JsonOptions);
        SafetyRatingsVehicleRef? vehicleRef = lookup?.Results?.FirstOrDefault();
        if (vehicleRef is null)
        {
            return new SafetyRatingsResult(null, null, null, null, null, $"no NHTSA safety rating on file for {modelYear} {make} {model}");
        }

        string detailUrl = $"https://api.nhtsa.gov/SafetyRatings/VehicleId/{vehicleRef.VehicleId}";
        using HttpResponseMessage detailResponse = await http.GetAsync(detailUrl, cancellationToken);
        detailResponse.EnsureSuccessStatusCode();
        string detailBody = await detailResponse.Content.ReadAsStringAsync(cancellationToken);

        SafetyRatingsDetailEnvelope? detail = JsonSerializer.Deserialize<SafetyRatingsDetailEnvelope>(detailBody, JsonOptions);
        SafetyRatingsDetailRecord? record = detail?.Results?.FirstOrDefault();
        if (record is null)
        {
            return new SafetyRatingsResult(null, null, null, null, vehicleRef.VehicleDescription, "NHTSA returned no rating detail for this vehicle");
        }

        return new SafetyRatingsResult(
            OverallRating: ParseStars(record.OverallRating),
            FrontRating: ParseStars(record.OverallFrontCrashRating),
            SideRating: ParseStars(record.OverallSideCrashRating),
            RolloverRating: ParseStars(record.RolloverRating),
            VehicleDescription: vehicleRef.VehicleDescription,
            ErrorText: null);
    }

    // NHTSA reports each category as a string: a digit "1".."5", or "Not Rated" for an untested
    // category; anything that doesn't parse as a star count is treated as untested rather than 0.
    private static int? ParseStars(string? value) => int.TryParse(value, out int stars) ? stars : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record SafetyRatingsLookupEnvelope(List<SafetyRatingsVehicleRef>? Results);

    private sealed record SafetyRatingsVehicleRef(int VehicleId, string? VehicleDescription);

    private sealed record SafetyRatingsDetailEnvelope(List<SafetyRatingsDetailRecord>? Results);

    private sealed record SafetyRatingsDetailRecord(
        string? OverallRating,
        string? OverallFrontCrashRating,
        string? OverallSideCrashRating,
        string? RolloverRating);

    private sealed record DecodeEnvelope([property: JsonPropertyName("Results")] List<DecodeRecord>? Results);

    private sealed record DecodeRecord(
        [property: JsonPropertyName("VIN")] string? Vin,
        string? ModelYear,
        string? Make,
        string? Model,
        string? Trim,
        string? BodyClass,
        string? EngineCylinders,
        string? ErrorText);

    private sealed record RecallsEnvelope(List<RecallRecord>? Results);

    private sealed record RecallRecord(
        string? NHTSACampaignNumber,
        string? Component,
        string? Summary,
        string? Consequence,
        string? Remedy,
        string? ReportReceivedDate);

    private sealed record ComplaintsEnvelope(int Count);
}
