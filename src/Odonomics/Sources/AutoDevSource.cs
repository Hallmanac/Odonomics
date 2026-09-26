using System.Text.Json;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Sources;

/// <summary>Auto.dev listings API, free tier. Structured JSON with VIN already on it: no model
/// extraction needed. Ported from spike/Sources/AutoDevSource.cs.
/// <para>Used cars only. The listings endpoint takes a repeatable <c>condition</c> query parameter,
/// checked against the live endpoint on 2026-09-26 (the public docs do not list it): each
/// <c>condition=</c> value selects the records whose <c>condition</c> property equals it,
/// lower-case, and repeating the parameter ORs the values. The property carries <c>used</c>,
/// <c>certified pre-owned</c> and <c>new</c>. <c>condition=used</c> alone drops the certified
/// pre-owned records, which are used cars, and a comma-joined value matches nothing, so the URL
/// repeats the parameter for <c>used</c> and <c>certified pre-owned</c>. As a backstop against the
/// filter being ignored, a record whose <c>condition</c> says <c>new</c> is rejected; a record with
/// no <c>condition</c> is kept.</para></summary>
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
                             $"&make={query.Make}&model={model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}" +
                             "&condition=used&condition=certified%20pre-owned";

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
        string? condition = record.TryGetProperty("condition", out JsonElement cd) && cd.ValueKind == JsonValueKind.String ? cd.GetString() : null;

        if (string.IsNullOrWhiteSpace(vin))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, no VIN");
            return;
        }

        if (string.Equals(condition, "new", StringComparison.OrdinalIgnoreCase))
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

        if (PlaceholderPrice.IsBelowFloor(price.Value))
        {
            result.Rejections.Add($"{query.Make} {query.Model}: candidate rejected, placeholder price {price.Value:0.##} ({vin})");
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
