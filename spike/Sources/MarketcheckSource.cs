using System.Diagnostics;
using System.Text.Json;
using Spike.Models;

namespace Spike.Sources;

/// <summary>Marketcheck search API, on whatever trial tier the key carries. Structured JSON with VIN: no extraction needed.</summary>
public sealed class MarketcheckSource(string? apiKey, RecordedResponses recorded) : IListingSource
{
    public string Name => "marketcheck";

    public async Task<SourceRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new SourceRunResult { Source = Name };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            result.CouldNotRun = true;
            result.CouldNotRunReason = "no key: Marketcheck:ApiKey not set in user-secrets id odonomics-spike and MARKETCHECK__APIKEY not set in the environment";
            return result;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (QueryGroup group in QueryGroup.All)
        {
            try
            {
                int yearMax = group.YearMax ?? DateTime.UtcNow.Year + 1;
                string model = Uri.EscapeDataString(group.Model);
                string url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                           $"?api_key={apiKey}&zip={group.Zip}&radius={group.RadiusMiles}" +
                           $"&make={group.Make}&model={model}&year_range={group.YearMin}-{yearMax}&miles_range=0-{group.MaxMileage}";

                HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                string fileName = $"{group.Make}-{group.Model}".Replace(" ", "_") + ".json";
                string rawPath = await recorded.WriteAsync(Name, fileName, body, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Failures.Add($"{group.Make} {group.Model}: HTTP {(int)response.StatusCode}: {SecretRedactor.Redact(body)}");
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("listings", out JsonElement listings))
                {
                    result.Failures.Add($"{group.Make} {group.Model}: no 'listings' field in response");
                    continue;
                }

                foreach (JsonElement listing in listings.EnumerateArray())
                {
                    JsonElement build = listing.TryGetProperty("build", out JsonElement b) ? b : default;
                    int? year = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("year", out JsonElement y)
                        ? y.GetInt32()
                        : null;
                    int? mileage = listing.TryGetProperty("miles", out JsonElement m) ? m.GetInt32() : null;

                    if (!group.MatchesYear(year) || !group.MatchesMileage(mileage))
                    {
                        continue;
                    }

                    result.Candidates.Add(new Candidate
                    {
                        Source = Name,
                        Vin = listing.TryGetProperty("vin", out JsonElement vin) ? vin.GetString() : null,
                        Year = year,
                        Make = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("make", out JsonElement mk) ? mk.GetString() : group.Make,
                        Model = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("model", out JsonElement md) ? md.GetString() : group.Model,
                        Trim = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("trim", out JsonElement tr) ? tr.GetString() : null,
                        Price = listing.TryGetProperty("price", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null,
                        Mileage = mileage,
                        Url = listing.TryGetProperty("vdp_url", out JsonElement u) ? u.GetString() : null,
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
        result.DollarsSpent = 0m; // trial tier; see findings for quota/pricing notes
        return result;
    }
}
