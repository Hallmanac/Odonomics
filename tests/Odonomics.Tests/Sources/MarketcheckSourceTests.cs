using Odonomics.Sources;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Sources;

/// <summary>Contract test against a real Marketcheck response recorded by the spike
/// (tests/Odonomics.Tests/fixtures/sources/marketcheck/).</summary>
public class MarketcheckSourceTests
{
    private static string FixturePath => Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "sources", "marketcheck", "honda-insight.json");

    [Fact]
    public async Task RunAsync_NoApiKey_ReportsCouldNotRun()
    {
        var source = new MarketcheckSource(apiKey: null, new HttpClient());

        SourceResult result = await source.RunAsync([new ListingQuery("Honda", "Insight", 2019, "32114", 50, 100000)], CancellationToken.None);

        Assert.True(result.CouldNotRun);
        Assert.Contains("Marketcheck:ApiKey", result.CouldNotRunReason);
    }

    [Fact]
    public async Task RunAsync_RecordedFixture_ParsesBuildFieldsAndVin()
    {
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        int yearMax = DateTime.UtcNow.Year + 1;
        string url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                     $"?api_key=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_range={query.YearMin}-{yearMax}&miles_range=0-{query.MaxMileage}";
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath),
        });
        var source = new MarketcheckSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.False(result.CouldNotRun);
        Assert.Equal(2, result.Candidates.Count);
        var candidate = Assert.Single(result.Candidates, c => c.Vin == "19XZE4F52ME000999");
        Assert.Equal("Honda", candidate.Make);
        Assert.Equal("Insight", candidate.Model);
        Assert.Equal("EX", candidate.Trim);
        Assert.Equal(2021, candidate.Year);
        Assert.Equal("marketcheck", candidate.Source);
        Assert.Equal(["Insight"], result.ModelsCovered);
    }

    [Fact]
    public async Task RunAsync_QueryFails_ModelIsNotReportedCovered()
    {
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        var handler = new StatusCodeHttpMessageHandler(System.Net.HttpStatusCode.Unauthorized);
        var source = new MarketcheckSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.False(result.CouldNotRun);
        Assert.Empty(result.ModelsCovered);
        Assert.Contains(result.Rejections, r => r.Contains("HTTP 401"));
    }
}
