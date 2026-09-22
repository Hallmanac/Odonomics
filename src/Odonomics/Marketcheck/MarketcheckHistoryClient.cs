using System.Text.Json;

namespace Odonomics.Marketcheck;

/// <summary>One prior (or current) listing of a VIN, as Marketcheck's history endpoint reports it.
/// <see cref="Mileage"/> is null for a "new" inventory_type entry, which never carries an odometer
/// reading.</summary>
public sealed record VinHistoryListing(
    string? Dealer,
    string? City,
    string? State,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    decimal? Price,
    int? Mileage,
    string? Url);

/// <summary><see cref="CouldNotFetchReason"/> is set, never thrown, on a missing key or a failed
/// call: the same "degrade gracefully" contract MarketcheckSource already follows for search.</summary>
public sealed record VinHistoryResult(
    IReadOnlyList<VinHistoryListing> PriorListings,
    int? CurrentListingDaysOnMarket,
    string? CouldNotFetchReason);

/// <summary>Marketcheck's VIN history API: every prior listing recorded for a VIN, plus the current
/// listing's days-on-market ("dom") pulled from an active-search-by-VIN lookup. Ported from the
/// spike's MarketcheckSource pattern of degrading to a reported reason rather than throwing.</summary>
public sealed class MarketcheckHistoryClient(string? apiKey, HttpClient http)
{
    public async Task<VinHistoryResult> GetHistoryAsync(string vin, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new VinHistoryResult([], null, "no key: Marketcheck:ApiKey not set in user-secrets id odonomics and MARKETCHECK__APIKEY not set in the environment");
        }

        try
        {
            IReadOnlyList<VinHistoryListing> priorListings = await FetchPriorListingsAsync(vin, cancellationToken);
            int? daysOnMarket = await FetchCurrentDaysOnMarketAsync(vin, cancellationToken);
            return new VinHistoryResult(priorListings, daysOnMarket, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new VinHistoryResult([], null, ex.Message);
        }
    }

    private async Task<IReadOnlyList<VinHistoryListing>> FetchPriorListingsAsync(string vin, CancellationToken cancellationToken)
    {
        string url = $"https://mc-api.marketcheck.com/v2/history/car/{Uri.EscapeDataString(vin)}?api_key={apiKey}";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Marketcheck VIN history: HTTP {(int)response.StatusCode}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        List<VinHistoryListing> listings = [];
        foreach (JsonElement entry in doc.RootElement.EnumerateArray())
        {
            listings.Add(new VinHistoryListing(
                Dealer: GetString(entry, "seller_name"),
                City: GetString(entry, "city"),
                State: GetString(entry, "state"),
                FirstSeen: GetDate(entry, "first_seen_at_date"),
                LastSeen: GetDate(entry, "last_seen_at_date"),
                Price: GetDecimal(entry, "price"),
                Mileage: GetInt(entry, "miles"),
                Url: GetString(entry, "vdp_url")));
        }

        return listings;
    }

    private async Task<int?> FetchCurrentDaysOnMarketAsync(string vin, CancellationToken cancellationToken)
    {
        string url = $"https://mc-api.marketcheck.com/v2/search/car/active?api_key={apiKey}&vin={Uri.EscapeDataString(vin)}";
        using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Marketcheck current listing lookup: HTTP {(int)response.StatusCode}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("listings", out JsonElement listings) || listings.GetArrayLength() == 0)
        {
            return null; // not currently listed anywhere Marketcheck tracks; not a fetch failure
        }

        return GetInt(listings[0], "dom");
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static decimal? GetDecimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static DateTimeOffset? GetDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out DateTimeOffset parsed) ? parsed : null;
}
