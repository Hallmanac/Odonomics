using System.Net;
using System.Net.Http.Headers;
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
    /// <summary>Marketcheck rate-limits under load with HTTP 429; observed alongside the 158-VIN
    /// outage of 2026-09-28 where every VIN-history call in a batch came back 429 and was recorded
    /// as a permanent could-not-fetch rather than retried. Bounded so a sustained outage still
    /// degrades gracefully instead of retrying forever.</summary>
    private const int MaxAttempts = 4;

    /// <summary>Used when a 429 response carries no Retry-After header.</summary>
    private static readonly TimeSpan DefaultRetryPause = TimeSpan.FromSeconds(1);

    /// <summary>The longest this client waits out a single Retry-After, however long the header
    /// itself asks for. Marketcheck's own quota-exhausted 429 has been observed carrying a
    /// Retry-After well over an hour; honoring that verbatim would stall a whole `odo research`
    /// batch on the first rate-limited vehicle, since <see cref="MaxAttempts"/> bounds only the
    /// attempt count, not the wait between them. Past this cap, the wait is no longer "worth
    /// retrying" in a batch's timeframe, so it degrades to a could-not-fetch reason instead.</summary>
    private static readonly TimeSpan MaxRetryPause = TimeSpan.FromSeconds(5);

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
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new VinHistoryResult([], null, ex.Message);
        }
    }

    private async Task<IReadOnlyList<VinHistoryListing>> FetchPriorListingsAsync(string vin, CancellationToken cancellationToken)
    {
        string url = $"https://mc-api.marketcheck.com/v2/history/car/{Uri.EscapeDataString(vin)}?api_key={apiKey}";
        (HttpStatusCode status, string body) = await GetWithRetryOn429Async(url, cancellationToken);
        if (!IsSuccess(status))
        {
            throw new InvalidOperationException($"Marketcheck VIN history: HTTP {(int)status}");
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
        (HttpStatusCode status, string body) = await GetWithRetryOn429Async(url, cancellationToken);
        if (!IsSuccess(status))
        {
            throw new InvalidOperationException($"Marketcheck current listing lookup: HTTP {(int)status}");
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("listings", out JsonElement listings) || listings.GetArrayLength() == 0)
        {
            return null; // not currently listed anywhere Marketcheck tracks; not a fetch failure
        }

        return GetInt(listings[0], "dom");
    }

    /// <summary>Runs one GET against <paramref name="url"/>, retrying up to <see cref="MaxAttempts"/>
    /// times when the response is HTTP 429: Marketcheck's rate-limit response, honoring the
    /// response's Retry-After header (either a delta-seconds or an HTTP-date form) when present and
    /// falling back to <see cref="DefaultRetryPause"/> otherwise, capped at <see cref="MaxRetryPause"/>
    /// either way. Any other status, success or failure, is returned immediately on the first
    /// attempt: only 429 is worth waiting out.</summary>
    private async Task<(HttpStatusCode Status, string Body)> GetWithRetryOn429Async(string url, CancellationToken cancellationToken)
    {
        int attempt = 0;
        while (true)
        {
            attempt++;
            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt >= MaxAttempts)
            {
                return (response.StatusCode, body);
            }

            TimeSpan delay = RetryAfterDelay(response) ?? DefaultRetryPause;
            await Task.Delay(delay > MaxRetryPause ? MaxRetryPause : delay, cancellationToken);
        }
    }

    private static TimeSpan? RetryAfterDelay(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (retryAfter.Date is DateTimeOffset date)
        {
            TimeSpan untilDate = date - DateTimeOffset.UtcNow;
            return untilDate > TimeSpan.Zero ? untilDate : TimeSpan.Zero;
        }

        return null;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

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
