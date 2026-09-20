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

        var stopwatch = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        foreach (var group in QueryGroup.All)
        {
            try
            {
                var yearMax = group.YearMax ?? DateTime.UtcNow.Year + 1;
                var model = Uri.EscapeDataString(group.Model);
                var url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                           $"?api_key={apiKey}&zip={group.Zip}&radius={group.RadiusMiles}" +
                           $"&make={group.Make}&model={model}&year_range={group.YearMin}-{yearMax}&miles_range=0-{group.MaxMileage}";

                var response = await http.GetAsync(url, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                var fileName = $"{group.Make}-{group.Model}".Replace(" ", "_") + ".json";
                var rawPath = await recorded.WriteAsync(Name, fileName, body, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Failures.Add($"{group.Make} {group.Model}: HTTP {(int)response.StatusCode}: {SecretRedactor.Redact(body)}");
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("listings", out var listings))
                {
                    result.Failures.Add($"{group.Make} {group.Model}: no 'listings' field in response");
                    continue;
                }

                foreach (var listing in listings.EnumerateArray())
                {
                    var build = listing.TryGetProperty("build", out var b) ? b : default;
                    var year = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("year", out var y)
                        ? y.GetInt32()
                        : (int?)null;
                    var mileage = listing.TryGetProperty("miles", out var m) ? m.GetInt32() : (int?)null;

                    if (!group.MatchesYear(year) || !group.MatchesMileage(mileage))
                    {
                        continue;
                    }

                    result.Candidates.Add(new Candidate
                    {
                        Source = Name,
                        Vin = listing.TryGetProperty("vin", out var vin) ? vin.GetString() : null,
                        Year = year,
                        Make = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("make", out var mk) ? mk.GetString() : group.Make,
                        Model = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("model", out var md) ? md.GetString() : group.Model,
                        Trim = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("trim", out var tr) ? tr.GetString() : null,
                        Price = listing.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null,
                        Mileage = mileage,
                        Url = listing.TryGetProperty("vdp_url", out var u) ? u.GetString() : null,
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
