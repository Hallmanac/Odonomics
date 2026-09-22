using System.Net;
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
}
