using System.Net;
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

    /// <summary>api.nhtsa.gov sits behind Akamai, which has been observed (2026-09-22) answering
    /// an otherwise-healthy VIN's recalls/complaints/safety-ratings call with an HTTP 200 whose
    /// body is an HTML error page ("Error", "Reference #102...") rather than JSON, moments before
    /// or after the same URL answers cleanly. One retry after a short pause clears it; see
    /// FetchAsync.</summary>
    private static readonly TimeSpan RetryPause = TimeSpan.FromMilliseconds(500);

    private const int MaxAttempts = 2;

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

    public async Task<RecallsResult> GetRecallsAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string url = $"https://api.nhtsa.gov/recalls/recallsByVehicle?make={Uri.EscapeDataString(make)}&model={Uri.EscapeDataString(model)}&modelYear={modelYear}";

        // Not EnsureSuccessStatusCode() in the parse delegate: this endpoint was found, by hand,
        // to return HTTP 400 together with a perfectly valid "Count":0 body on a make/model/year
        // with zero recalls, while still returning 200 whenever at least one recall exists. The
        // body is always the real signal here; a genuinely malformed response still fails below at
        // deserialization, which FetchAsync retries once and then degrades gracefully.
        NhtsaCallOutcome<RecallsEnvelope?> outcome = await FetchAsync(
            url,
            (_, body) => JsonSerializer.Deserialize<RecallsEnvelope>(body, JsonOptions),
            cancellationToken);

        if (outcome.Failure is NhtsaFailure failure)
        {
            return new RecallsResult([], Describe("NHTSA recalls", failure));
        }

        IEnumerable<RecallRecord> records = outcome.Value?.Results ?? [];
        return new RecallsResult([.. records.Select(r => new RecallEntry(
            CampaignNumber: r.NHTSACampaignNumber ?? "",
            Component: r.Component ?? "",
            Summary: r.Summary ?? "",
            Consequence: r.Consequence ?? "",
            Remedy: r.Remedy ?? "",
            ReportReceivedDate: r.ReportReceivedDate ?? ""))], null);
    }

    public async Task<ComplaintsResult> GetComplaintCountAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string url = $"https://api.nhtsa.gov/complaints/complaintsByVehicle?make={Uri.EscapeDataString(make)}&model={Uri.EscapeDataString(model)}&modelYear={modelYear}";

        // Not EnsureSuccessStatusCode(): carries the same quirk as recallsByVehicle above, HTTP 400
        // with a perfectly valid "count":0 body for a make/model/year with zero complaints, most
        // often a brand-new model year. The body is the real signal.
        NhtsaCallOutcome<ComplaintsEnvelope?> outcome = await FetchAsync(
            url,
            (_, body) => JsonSerializer.Deserialize<ComplaintsEnvelope>(body, JsonOptions),
            cancellationToken);

        return outcome.Failure is NhtsaFailure failure
            ? new ComplaintsResult(0, Describe("NHTSA complaints", failure))
            : new ComplaintsResult(outcome.Value?.Count ?? 0, null);
    }

    /// <summary>NHTSA's SafetyRatings API is two calls: look up the VehicleId(s) on file for this
    /// year/make/model, then fetch the star ratings for one of them. A year/make/model can have
    /// several VehicleIds (one per body style/trim NHTSA tested separately); v0 takes the first, the
    /// same simplification the brief allows for an uncertain detail rather than guessing which body
    /// style matches this specific VIN.</summary>
    public async Task<SafetyRatingsResult> GetSafetyRatingsAsync(string make, string model, int modelYear, CancellationToken cancellationToken)
    {
        string lookupUrl = $"https://api.nhtsa.gov/SafetyRatings/modelyear/{modelYear}/make/{Uri.EscapeDataString(make)}/model/{Uri.EscapeDataString(model)}";
        NhtsaCallOutcome<SafetyRatingsLookupEnvelope?> lookupOutcome = await FetchAsync(
            lookupUrl,
            (response, body) =>
            {
                response.EnsureSuccessStatusCode();
                return JsonSerializer.Deserialize<SafetyRatingsLookupEnvelope>(body, JsonOptions);
            },
            cancellationToken);

        if (lookupOutcome.Failure is NhtsaFailure lookupFailure)
        {
            return new SafetyRatingsResult(null, null, null, null, null, null, Describe("NHTSA safety ratings", lookupFailure));
        }

        SafetyRatingsVehicleRef? vehicleRef = lookupOutcome.Value?.Results?.FirstOrDefault();
        if (vehicleRef is null)
        {
            return new SafetyRatingsResult(null, null, null, null, null, $"no NHTSA safety rating on file for {modelYear} {make} {model}", null);
        }

        string detailUrl = $"https://api.nhtsa.gov/SafetyRatings/VehicleId/{vehicleRef.VehicleId}";
        NhtsaCallOutcome<SafetyRatingsDetailEnvelope?> detailOutcome = await FetchAsync(
            detailUrl,
            (response, body) =>
            {
                response.EnsureSuccessStatusCode();
                return JsonSerializer.Deserialize<SafetyRatingsDetailEnvelope>(body, JsonOptions);
            },
            cancellationToken);

        if (detailOutcome.Failure is NhtsaFailure detailFailure)
        {
            return new SafetyRatingsResult(null, null, null, null, vehicleRef.VehicleDescription, null, Describe("NHTSA safety ratings", detailFailure));
        }

        SafetyRatingsDetailRecord? record = detailOutcome.Value?.Results?.FirstOrDefault();
        if (record is null)
        {
            return new SafetyRatingsResult(null, null, null, null, vehicleRef.VehicleDescription, "NHTSA returned no rating detail for this vehicle", null);
        }

        return new SafetyRatingsResult(
            OverallRating: ParseStars(record.OverallRating),
            FrontRating: ParseStars(record.OverallFrontCrashRating),
            SideRating: ParseStars(record.OverallSideCrashRating),
            RolloverRating: ParseStars(record.RolloverRating),
            VehicleDescription: vehicleRef.VehicleDescription,
            ErrorText: null,
            CouldNotFetchReason: null);
    }

    // NHTSA reports each category as a string: a digit "1".."5", or "Not Rated" for an untested
    // category; anything that doesn't parse as a star count is treated as untested rather than 0.
    private static int? ParseStars(string? value) => int.TryParse(value, out int stars) ? stars : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Runs one GET-then-parse against <paramref name="url"/>, retrying once after
    /// <see cref="RetryPause"/> when <paramref name="parse"/> throws (a non-JSON body, including an
    /// Akamai HTML error page, or a non-success status the parse delegate chooses to reject via
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>). A second failure degrades to a
    /// <see cref="NhtsaCallOutcome{T}.Failure"/> carrying the HTTP status and the body's first line,
    /// never a raw exception.</summary>
    private async Task<NhtsaCallOutcome<T>> FetchAsync<T>(string url, Func<HttpResponseMessage, string, T> parse, CancellationToken cancellationToken)
    {
        HttpStatusCode status = HttpStatusCode.OK;
        string body = "";
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                return NhtsaCallOutcome<T>.Success(parse(response, body));
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException)
            {
                if (attempt == MaxAttempts)
                {
                    break;
                }

                await Task.Delay(RetryPause, cancellationToken);
            }
        }

        return NhtsaCallOutcome<T>.Failed(new NhtsaFailure((int)status, FirstLine(body)));
    }

    private static string Describe(string source, NhtsaFailure failure) =>
        $"{source} could not be fetched, HTTP {failure.HttpStatus} with {DescribeBody(failure.BodyFirstLine)}";

    private static string DescribeBody(string firstLine) =>
        firstLine.TrimStart().StartsWith('<') ? "an HTML error page" : $"an unexpected response: {firstLine}";

    private static string FirstLine(string body)
    {
        int newlineIndex = body.IndexOfAny(['\r', '\n']);
        string line = newlineIndex >= 0 ? body[..newlineIndex] : body;
        return line.Trim();
    }

    /// <summary>The outcome of one <see cref="FetchAsync{T}"/> call: either the parsed value, or a
    /// <see cref="NhtsaFailure"/> describing the final failed attempt.</summary>
    private sealed record NhtsaCallOutcome<T>(T? Value, NhtsaFailure? Failure)
    {
        public static NhtsaCallOutcome<T> Success(T value) => new(value, null);

        public static NhtsaCallOutcome<T> Failed(NhtsaFailure failure) => new(default, failure);
    }

    /// <summary>The HTTP status and the response body's first line from a call's final failed
    /// attempt, the detail a could-not-fetch reason is built from.</summary>
    private sealed record NhtsaFailure(int HttpStatus, string BodyFirstLine);

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
