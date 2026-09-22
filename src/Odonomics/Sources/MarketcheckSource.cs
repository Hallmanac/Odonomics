using System.Text.Json;
using Odonomics.Ledger;

namespace Odonomics.Sources;

/// <summary>Marketcheck search API, on whatever trial tier the key carries. Structured JSON with
/// VIN: no extraction needed. Ported from spike/Sources/MarketcheckSource.cs.</summary>
public sealed class MarketcheckSource(string? apiKey, HttpClient http) : IListingSource
{
    public string Name => "marketcheck";

    public async Task<SourceResult> RunAsync(IReadOnlyList<ListingQuery> queries, CancellationToken cancellationToken)
    {
        var result = new SourceResult { Source = Name };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return result with
            {
                CouldNotRun = true,
                CouldNotRunReason = "no key: Marketcheck:ApiKey not set in user-secrets id odonomics and MARKETCHECK__APIKEY not set in the environment",
            };
        }

        foreach (ListingQuery query in queries)
        {
            try
            {
                int yearMax = DateTime.UtcNow.Year + 1;
                string model = Uri.EscapeDataString(query.Model);
                string url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                             $"?api_key={apiKey}&zip={query.Zip}&radius={query.RadiusMiles}" +
                             $"&make={query.Make}&model={model}&year_range={query.YearMin}-{yearMax}&miles_range=0-{query.MaxMileage}";

                HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Rejections.Add($"{query.Make} {query.Model}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("listings", out JsonElement listings))
                {
                    result.Rejections.Add($"{query.Make} {query.Model}: no 'listings' field in response");
                    continue;
                }

                result.ModelsCovered.Add(query.Model);
                foreach (JsonElement listing in listings.EnumerateArray())
                {
                    AddCandidateIfValid(result, query, listing);
                }
            }
            catch (Exception ex)
            {
                result.Rejections.Add($"{query.Make} {query.Model}: {ex.Message}");
            }
        }

        return result;
    }

    private static void AddCandidateIfValid(SourceResult result, ListingQuery query, JsonElement listing)
    {
        JsonElement build = listing.TryGetProperty("build", out JsonElement b) ? b : default;
        string? vin = listing.TryGetProperty("vin", out JsonElement vinEl) ? vinEl.GetString() : null;
        string? url = listing.TryGetProperty("vdp_url", out JsonElement u) ? u.GetString() : null;
        int? year = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("year", out JsonElement y) && y.ValueKind == JsonValueKind.Number
            ? y.GetInt32() : null;
        int? mileage = listing.TryGetProperty("miles", out JsonElement m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
        decimal? price = listing.TryGetProperty("price", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;
        string make = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("make", out JsonElement mk) ? mk.GetString() ?? query.Make : query.Make;
        string? trim = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("trim", out JsonElement tr) ? tr.GetString() : null;

        if (string.IsNullOrWhiteSpace(vin))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, no VIN");
            return;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, no URL ({vin})");
            return;
        }

        if (year is null || !query.MatchesYear(year) || mileage is null || !query.MatchesMileage(mileage) || price is null)
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, out of query range or missing price/mileage ({vin})");
            return;
        }

        result.Candidates.Add(new ListingCandidate
        {
            Vin = vin,
            Source = "marketcheck",
            Url = url,
            Year = year.Value,
            Make = make,
            // The canonical model queried, not the API's own "build.model" field: VehicleEntity.Model
            // has to match the model half of the "source:model" token ModelsCovered feeds into
            // RunSources.Key, and the API is free to return a bare model ("Camry") for a query on a
            // compound one ("Camry Hybrid").
            Model = query.Model,
            Trim = trim,
            Price = price.Value,
            Mileage = mileage.Value,
        });
    }
}
