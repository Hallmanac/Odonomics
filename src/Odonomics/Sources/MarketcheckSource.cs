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
                             $"&make={query.Make}&model={model}&year_range={query.YearMin}-{yearMax}&miles_range=0-{query.MaxMileage}&car_type=used";

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
        string? apiMake = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("make", out JsonElement mk) ? mk.GetString() : null;
        string? apiModel = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("model", out JsonElement md) ? md.GetString() : null;
        string? trim = build.ValueKind == JsonValueKind.Object && build.TryGetProperty("trim", out JsonElement tr) ? tr.GetString() : null;
        string make = apiMake ?? query.Make;
        string? inventoryType = listing.TryGetProperty("inventory_type", out JsonElement it) && it.ValueKind == JsonValueKind.String ? it.GetString() : null;
        JsonElement dealerObj = listing.TryGetProperty("dealer", out JsonElement de) ? de : default;
        string? dealerName = dealerObj.ValueKind == JsonValueKind.Object && dealerObj.TryGetProperty("name", out JsonElement dn) ? dn.GetString() : null;
        string? dealerCity = dealerObj.ValueKind == JsonValueKind.Object && dealerObj.TryGetProperty("city", out JsonElement dc) ? dc.GetString() : null;
        string? dealerState = dealerObj.ValueKind == JsonValueKind.Object && dealerObj.TryGetProperty("state", out JsonElement ds) ? ds.GetString() : null;

        if (string.IsNullOrWhiteSpace(vin))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, no VIN");
            return;
        }

        if (string.Equals(inventoryType, "new", StringComparison.OrdinalIgnoreCase))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, new car ({vin})");
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

        if (!query.MatchesExtractedVehicle(apiMake, apiModel, trim, year))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, returned vehicle doesn't match the query ({vin}: {apiMake} {apiModel} {trim})");
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
            // RunSources.Key. The API is free to return a bare model ("Camry") for a query on a
            // compound one ("Camry Hybrid"), but MatchesExtractedVehicle above has already rejected
            // any candidate that isn't actually this query's model, so stamping the canonical model
            // here never mislabels a vehicle the API returned for a different reason.
            Model = query.Model,
            Trim = trim,
            Price = price.Value,
            Mileage = mileage.Value,
            DealerName = dealerName,
            DealerLocation = DealerLocationFormat.Build(dealerCity, dealerState),
        });
    }
}
