using System.Diagnostics;
using System.Text.Json;
using Spike.Models;

namespace Spike.Sources;

/// <summary>Auto.dev listings API, free tier. Structured JSON with VIN already on it: no model extraction needed.</summary>
public sealed class AutoDevSource(string? apiKey, RecordedResponses recorded) : IListingSource
{
    public string Name => "auto.dev";

    public async Task<SourceRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new SourceRunResult { Source = Name };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            result.CouldNotRun = true;
            result.CouldNotRunReason = "no key: AutoDev:ApiKey not set in user-secrets id odonomics-spike and AUTODEV__APIKEY not set in the environment";
            return result;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (QueryGroup group in QueryGroup.All)
        {
            try
            {
                string yearMax = group.YearMax.HasValue ? $"&year_max={group.YearMax}" : "";
                string model = Uri.EscapeDataString(group.Model);
                string url = $"https://auto.dev/api/listings?apikey={apiKey}&zip={group.Zip}&radius={group.RadiusMiles}" +
                           $"&make={group.Make}&model={model}&year_min={group.YearMin}{yearMax}&mileage_max={group.MaxMileage}";

                HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                string fileName = $"{group.Make}-{group.Model}".Replace(" ", "_") + ".json";
                string rawPath = await recorded.WriteAsync(Name, fileName, body, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Failures.Add($"{group.Make} {group.Model}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("records", out JsonElement records))
                {
                    result.Failures.Add($"{group.Make} {group.Model}: no 'records' field in response");
                    continue;
                }

                foreach (JsonElement record in records.EnumerateArray())
                {
                    int? year = record.TryGetProperty("year", out JsonElement y) ? y.GetInt32() : null;
                    int? mileage = record.TryGetProperty("mileageUnformatted", out JsonElement m) ? m.GetInt32() : null;
                    if (!group.MatchesYear(year) || !group.MatchesMileage(mileage))
                    {
                        continue;
                    }

                    result.Candidates.Add(new Candidate
                    {
                        Source = Name,
                        Vin = record.TryGetProperty("vin", out JsonElement vin) ? vin.GetString() : null,
                        Year = year,
                        Make = record.TryGetProperty("make", out JsonElement mk) ? mk.GetString() : group.Make,
                        Model = record.TryGetProperty("model", out JsonElement md) ? md.GetString() : group.Model,
                        Trim = record.TryGetProperty("trim", out JsonElement tr) ? tr.GetString() : null,
                        Price = record.TryGetProperty("priceUnformatted", out JsonElement p) && p.ValueKind == JsonValueKind.Number
                            ? p.GetDecimal()
                            : null,
                        Mileage = mileage,
                        Url = record.TryGetProperty("vdpUrl", out JsonElement u) ? u.GetString() : null,
                        RawRecordPath = rawPath,
                        WasExtracted = false,
                    });
                }
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{group.Make} {group.Model}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        result.WallTime = stopwatch.Elapsed;
        result.DollarsSpent = 0m; // free tier
        return result;
    }
}
