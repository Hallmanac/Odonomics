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
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);

        ComplaintsEnvelope? envelope = JsonSerializer.Deserialize<ComplaintsEnvelope>(body, JsonOptions);
        return envelope?.Count ?? 0;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

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
