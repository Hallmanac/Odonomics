using System.Net;
using System.Net.Http.Headers;
using Odonomics.Marketcheck;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Marketcheck;

/// <summary>Contract tests against a real Marketcheck VIN-history response and a real
/// active-search-by-VIN response, both recorded to tests/Odonomics.Tests/fixtures/marketcheck/. No
/// live network call runs during the test suite; see FixtureHttpMessageHandler.</summary>
public class MarketcheckHistoryClientTests
{
    private const string Vin = "19XZE4F52ME000999";

    private static string FixturePath(string fileName) => Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "marketcheck", fileName);

    [Fact]
    public async Task GetHistoryAsync_NoApiKey_DegradesToCouldNotFetchReason()
    {
        var client = new MarketcheckHistoryClient(apiKey: null, new HttpClient());

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Empty(result.PriorListings);
        Assert.Null(result.CurrentListingDaysOnMarket);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("Marketcheck:ApiKey", result.CouldNotFetchReason);
    }

    [Fact]
    public async Task GetHistoryAsync_RecordedFixtures_ParsesPriorListingsAndDaysOnMarket()
    {
        string historyUrl = $"https://mc-api.marketcheck.com/v2/history/car/{Vin}?api_key=test-key";
        string activeUrl = $"https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin={Vin}";
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [historyUrl] = await File.ReadAllTextAsync(FixturePath($"vin-history-{Vin}.json")),
            [activeUrl] = await File.ReadAllTextAsync(FixturePath($"active-search-{Vin}.json")),
        });
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Equal(88, result.CurrentListingDaysOnMarket);
        Assert.Equal(7, result.PriorListings.Count);
        VinHistoryListing first = Assert.Single(result.PriorListings, l => l.Dealer == "Holler Classic");
        Assert.Equal("FL", first.State);
        Assert.Equal(17995m, first.Price);
        Assert.Equal(69599, first.Mileage);
        Assert.NotNull(first.FirstSeen);
        Assert.NotNull(first.LastSeen);
    }

    [Fact]
    public async Task GetHistoryAsync_HistoryCallFails_DegradesToCouldNotFetchReason()
    {
        var handler = new StatusCodeHttpMessageHandler(HttpStatusCode.Unauthorized);
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Empty(result.PriorListings);
        Assert.Null(result.CurrentListingDaysOnMarket);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("401", result.CouldNotFetchReason);
    }

    /// <summary>An HttpClient.Timeout expiry surfaces as a TaskCanceledException (an
    /// OperationCanceledException) even though nobody cancelled the caller's own token: this must
    /// still degrade to a reason rather than propagate, the same as any other failed call.</summary>
    [Fact]
    public async Task GetHistoryAsync_HttpClientTimesOutWithoutCallerCancelling_DegradesToCouldNotFetchReason()
    {
        var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing."));
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Empty(result.PriorListings);
        Assert.Null(result.CurrentListingDaysOnMarket);
        Assert.NotNull(result.CouldNotFetchReason);
    }

    /// <summary>Pins the 2026-09-28 fix: Marketcheck rate-limits under load with HTTP 429, and the
    /// call used to record that as a permanent could-not-fetch reason on the first attempt rather
    /// than backing off and trying again. The fake handler's first response is 429 with a
    /// Retry-After: 0 header (so the retry pause here is instant rather than slowing the suite down),
    /// its second is 200 with the recorded fixture, proving the retry both happens and honors
    /// Retry-After.</summary>
    [Fact]
    public async Task GetHistoryAsync_HistoryCallReturns429ThenSucceeds_RetriesAndStoresHistory()
    {
        string historyUrl = $"https://mc-api.marketcheck.com/v2/history/car/{Vin}?api_key=test-key";
        string activeUrl = $"https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin={Vin}";
        var handler = new TooManyRequestsThenFixtureHttpMessageHandler(
            historyUrl, await File.ReadAllTextAsync(FixturePath($"vin-history-{Vin}.json")),
            activeUrl, await File.ReadAllTextAsync(FixturePath($"active-search-{Vin}.json")));
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Equal(7, result.PriorListings.Count);
        Assert.Equal(2, handler.HistoryUrlCallCount);
    }

    /// <summary>A 429 that never clears must still degrade to a could-not-fetch reason rather than
    /// retrying forever: the bounded-attempts half of the same fix.</summary>
    [Fact]
    public async Task GetHistoryAsync_HistoryCallAlwaysReturns429_DegradesAfterBoundedAttempts()
    {
        var handler = new AlwaysTooManyRequestsHttpMessageHandler();
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        Assert.Empty(result.PriorListings);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("429", result.CouldNotFetchReason);
        Assert.True(handler.CallCount is > 1 and <= 4, $"expected a bounded 2-4 attempts, got {handler.CallCount}");
    }

    /// <summary>Pins the cap on <see cref="MarketcheckHistoryClient"/>'s Retry-After wait: a
    /// quota-exhausted 429 can carry a hours-long Retry-After, and honoring that verbatim would
    /// stall a whole `odo research` batch on the first rate-limited vehicle. The fake handler's
    /// first response asks for a one-hour wait; the test's own elapsed time proves the client
    /// didn't take it.</summary>
    [Fact]
    public async Task GetHistoryAsync_HistoryCallReturns429WithHourLongRetryAfter_CapsTheWait()
    {
        string historyUrl = $"https://mc-api.marketcheck.com/v2/history/car/{Vin}?api_key=test-key";
        string activeUrl = $"https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin={Vin}";
        var handler = new TooManyRequestsWithRetryAfterThenFixtureHttpMessageHandler(
            historyUrl, TimeSpan.FromHours(1), await File.ReadAllTextAsync(FixturePath($"vin-history-{Vin}.json")),
            activeUrl, await File.ReadAllTextAsync(FixturePath($"active-search-{Vin}.json")));
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        VinHistoryResult result = await client.GetHistoryAsync(Vin, CancellationToken.None);

        stopwatch.Stop();
        Assert.Null(result.CouldNotFetchReason);
        Assert.Equal(7, result.PriorListings.Count);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"expected the wait to be capped well under Retry-After's one hour, took {stopwatch.Elapsed}");
    }

    /// <summary>Pins the 2026-09-28 follow-up fix: after the 158-VIN outage where every vehicle in a
    /// batch spent its full per-call retries against HTTP 429 (about 40 minutes across the batch),
    /// three vehicles in a row still ending in 429 now stops the client from calling Marketcheck at
    /// all for the rest of the run, so the fourth and fifth vehicles below never generate a single
    /// request.</summary>
    [Fact]
    public async Task GetHistoryAsync_AlwaysReturns429AcrossManyVehicles_StopsCallingAfterThreeConsecutiveFailures()
    {
        var handler = new AlwaysTooManyRequestsHttpMessageHandler();
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));
        string[] vins = ["11111111111111111", "22222222222222222", "33333333333333333", "44444444444444444", "55555555555555555"];

        var results = new List<VinHistoryResult>();
        foreach (string vin in vins)
        {
            results.Add(await client.GetHistoryAsync(vin, CancellationToken.None));
        }

        List<string> vinsThatReachedMarketcheck = [.. vins.Where(v => handler.RequestedUrls.Any(u => u.Contains(v, StringComparison.Ordinal)))];
        Assert.Equal(3, vinsThatReachedMarketcheck.Count);
        Assert.Equal(vins.Take(3), vinsThatReachedMarketcheck);

        Assert.All(results.Take(3), r => Assert.Contains("429", r.CouldNotFetchReason));
        Assert.All(results.Skip(3), r => Assert.Equal(MarketcheckHistoryClient.AllowanceExhaustedReason, r.CouldNotFetchReason));
        Assert.True(client.AllowanceExhausted);
    }

    /// <summary>The other half of the same fix: a success in between two runs of failures must reset
    /// the consecutive-429 count, so four 429s spread across five vehicles (with a success in the
    /// middle) never trips the stop.</summary>
    [Fact]
    public async Task GetHistoryAsync_SuccessBetweenTwo429Vehicles_NeverExhaustsTheAllowance()
    {
        var handler = new TooManyRequestsExceptForOneVinHttpMessageHandler(
            Vin,
            await File.ReadAllTextAsync(FixturePath($"vin-history-{Vin}.json")),
            await File.ReadAllTextAsync(FixturePath($"active-search-{Vin}.json")));
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));

        VinHistoryResult first = await client.GetHistoryAsync("11111111111111111", CancellationToken.None);
        VinHistoryResult second = await client.GetHistoryAsync("22222222222222222", CancellationToken.None);
        VinHistoryResult third = await client.GetHistoryAsync(Vin, CancellationToken.None);
        VinHistoryResult fourth = await client.GetHistoryAsync("33333333333333333", CancellationToken.None);
        VinHistoryResult fifth = await client.GetHistoryAsync("44444444444444444", CancellationToken.None);

        Assert.Contains("429", first.CouldNotFetchReason);
        Assert.Contains("429", second.CouldNotFetchReason);
        Assert.Null(third.CouldNotFetchReason);
        Assert.Contains("429", fourth.CouldNotFetchReason);
        Assert.Contains("429", fifth.CouldNotFetchReason);
        Assert.NotEqual(MarketcheckHistoryClient.AllowanceExhaustedReason, fourth.CouldNotFetchReason);
        Assert.NotEqual(MarketcheckHistoryClient.AllowanceExhaustedReason, fifth.CouldNotFetchReason);
        Assert.False(client.AllowanceExhausted);
    }

    [Fact]
    public async Task GetHistoryAsync_CallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        var handler = new ThrowingHttpMessageHandler(new OperationCanceledException(cts.Token));
        var client = new MarketcheckHistoryClient("test-key", new HttpClient(handler));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetHistoryAsync(Vin, cts.Token));
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    /// <summary>Answers <paramref name="historyUrl"/> with HTTP 429 (Retry-After: 0) the first time
    /// and <paramref name="historyBody"/> after that; always answers <paramref name="activeUrl"/>
    /// with <paramref name="activeBody"/>, since the active-search call isn't what this test is
    /// exercising.</summary>
    private sealed class TooManyRequestsThenFixtureHttpMessageHandler(string historyUrl, string historyBody, string activeUrl, string activeBody) : HttpMessageHandler
    {
        public int HistoryUrlCallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
            if (url == activeUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(activeBody) });
            }

            if (url != historyUrl)
            {
                throw new InvalidOperationException($"unexpected URL {url}");
            }

            HistoryUrlCallCount++;
            if (HistoryUrlCallCount == 1)
            {
                var tooManyRequests = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
                tooManyRequests.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(tooManyRequests);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(historyBody) });
        }
    }

    /// <summary>Answers <paramref name="historyUrl"/> with HTTP 429 and a Retry-After of
    /// <paramref name="retryAfter"/> the first time, and <paramref name="historyBody"/> after that;
    /// always answers <paramref name="activeUrl"/> with <paramref name="activeBody"/>.</summary>
    private sealed class TooManyRequestsWithRetryAfterThenFixtureHttpMessageHandler(
        string historyUrl, TimeSpan retryAfter, string historyBody, string activeUrl, string activeBody) : HttpMessageHandler
    {
        private int _historyUrlCallCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
            if (url == activeUrl)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(activeBody) });
            }

            if (url != historyUrl)
            {
                throw new InvalidOperationException($"unexpected URL {url}");
            }

            _historyUrlCallCount++;
            if (_historyUrlCallCount == 1)
            {
                var tooManyRequests = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
                tooManyRequests.Headers.RetryAfter = new RetryConditionHeaderValue(retryAfter);
                return Task.FromResult(tooManyRequests);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(historyBody) });
        }
    }

    /// <summary>Answers every request with HTTP 429 (Retry-After: 0, so a bounded-attempts test never
    /// waits out a real backoff), forcing the client to eventually give up rather than retry
    /// forever.</summary>
    private sealed class AlwaysTooManyRequestsHttpMessageHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestedUrls.Add(request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL"));
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }

    /// <summary>Answers HTTP 429 for every VIN except <paramref name="successVin"/>, which gets the
    /// recorded history and active-search fixtures instead: proves a success in between two runs of
    /// 429s resets <see cref="MarketcheckHistoryClient"/>'s consecutive-failure count.</summary>
    private sealed class TooManyRequestsExceptForOneVinHttpMessageHandler(string successVin, string historyBody, string activeBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
            if (url.Contains(successVin, StringComparison.Ordinal))
            {
                string body = url.Contains("/history/car/", StringComparison.Ordinal) ? historyBody : activeBody;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            }

            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
