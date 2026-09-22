using System.Text.Json;
using Odonomics.Ledger;

namespace Odonomics.Sources;

/// <summary>Auto.dev listings API, free tier. Structured JSON with VIN already on it: no model
/// extraction needed. Ported from spike/Sources/AutoDevSource.cs.</summary>
public sealed class AutoDevSource(string? apiKey, HttpClient http) : IListingSource
{
    public string Name => "auto.dev";

    public async Task<SourceResult> RunAsync(IReadOnlyList<ListingQuery> queries, CancellationToken cancellationToken)
    {
        var result = new SourceResult { Source = Name };

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return result with
            {
                CouldNotRun = true,
                CouldNotRunReason = "no key: AutoDev:ApiKey not set in user-secrets id odonomics and AUTODEV__APIKEY not set in the environment",
            };
        }

        foreach (ListingQuery query in queries)
        {
            try
            {
                string model = Uri.EscapeDataString(query.Model);
                string url = $"https://auto.dev/api/listings?apikey={apiKey}&zip={query.Zip}&radius={query.RadiusMiles}" +
                             $"&make={query.Make}&model={model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}";

                HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    result.Rejections.Add($"{query.Make} {query.Model}: HTTP {(int)response.StatusCode}");
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("records", out JsonElement records))
                {
                    result.Rejections.Add($"{query.Make} {query.Model}: no 'records' field in response");
                    continue;
                }

                result.ModelsCovered.Add(query.Model);
                foreach (JsonElement record in records.EnumerateArray())
                {
                    AddCandidateIfValid(result, query, record);
                }
            }
            catch (Exception ex)
            {
                result.Rejections.Add($"{query.Make} {query.Model}: {ex.Message}");
            }
        }

        return result;
    }

    private static void AddCandidateIfValid(SourceResult result, ListingQuery query, JsonElement record)
    {
        string? vin = record.TryGetProperty("vin", out JsonElement vinEl) ? vinEl.GetString() : null;
        string? url = record.TryGetProperty("vdpUrl", out JsonElement u) ? u.GetString() : null;
        int? year = record.TryGetProperty("year", out JsonElement y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : null;
        int? mileage = record.TryGetProperty("mileageUnformatted", out JsonElement m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
        decimal? price = record.TryGetProperty("priceUnformatted", out JsonElement p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;
        string? apiMake = record.TryGetProperty("make", out JsonElement mk) ? mk.GetString() : null;
        string? apiModel = record.TryGetProperty("model", out JsonElement md) ? md.GetString() : null;
        string? trim = record.TryGetProperty("trim", out JsonElement tr) ? tr.GetString() : null;
        string make = apiMake ?? query.Make;
        string? dealerName = record.TryGetProperty("dealerName", out JsonElement dn) ? dn.GetString() : null;
        string? dealerCity = record.TryGetProperty("city", out JsonElement dc) ? dc.GetString() : null;
        string? dealerState = record.TryGetProperty("state", out JsonElement ds) ? ds.GetString() : null;

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

        if (!query.MatchesExtractedVehicle(apiMake, apiModel, trim))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, returned vehicle doesn't match the query ({vin}: {apiMake} {apiModel} {trim})");
            return;
        }

        result.Candidates.Add(new ListingCandidate
        {
            Vin = vin,
            Source = "auto.dev",
            Url = url,
            Year = year.Value,
            Make = make,
            // The canonical model queried, not the API's own "model" field: VehicleEntity.Model has
            // to match the model half of the "source:model" token ModelsCovered feeds into
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
