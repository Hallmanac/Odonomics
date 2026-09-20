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

        var stopwatch = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (var group in QueryGroup.All)
        {
            try
            {
                var yearMax = group.YearMax.HasValue ? $"&year_max={group.YearMax}" : "";
                var model = Uri.EscapeDataString(group.Model);
                var url = $"https://auto.dev/api/listings?apikey={apiKey}&zip={group.Zip}&radius={group.RadiusMiles}" +
                           $"&make={group.Make}&model={model}&year_min={group.YearMin}{yearMax}&mileage_max={group.MaxMileage}";

                var response = await http.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                var fileName = $"{group.Make}-{group.Model}".Replace(" ", "_") + ".json";
                var rawPath = await recorded.WriteAsync(Name, fileName, body, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Failures.Add($"{group.Make} {group.Model}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("records", out var records))
                {
                    result.Failures.Add($"{group.Make} {group.Model}: no 'records' field in response");
                    continue;
                }

                foreach (var record in records.EnumerateArray())
                {
                    var year = record.TryGetProperty("year", out var y) ? y.GetInt32() : (int?)null;
                    var mileage = record.TryGetProperty("mileageUnformatted", out var m) ? m.GetInt32() : (int?)null;
                    if (!group.MatchesYear(year) || !group.MatchesMileage(mileage))
                    {
                        continue;
                    }

                    result.Candidates.Add(new Candidate
                    {
                        Source = Name,
                        Vin = record.TryGetProperty("vin", out var vin) ? vin.GetString() : null,
                        Year = year,
                        Make = record.TryGetProperty("make", out var mk) ? mk.GetString() : group.Make,
                        Model = record.TryGetProperty("model", out var md) ? md.GetString() : group.Model,
                        Trim = record.TryGetProperty("trim", out var tr) ? tr.GetString() : null,
                        Price = record.TryGetProperty("priceUnformatted", out var p) && p.ValueKind == JsonValueKind.Number
                            ? p.GetDecimal()
                            : null,
                        Mileage = mileage,
                        Url = record.TryGetProperty("vdpUrl", out var u) ? u.GetString() : null,
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
