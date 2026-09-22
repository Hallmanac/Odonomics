using Odonomics.Ledger;
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
        Assert.Equal("Driver's Mart Sanford", candidate.DealerName);
        Assert.Equal("Sanford, FL", candidate.DealerLocation);
    }

    [Fact]
    public async Task RunAsync_ApiReturnsADifferentModelStringThanQueried_CandidateModelStaysCanonical()
    {
        // build.model is free to differ from the model this run actually queried for (a bare
        // "Insight" for a compound query, or the reverse); the candidate has to carry the queried
        // model, since that's the model half of the "source:model" token RunSources.Key stamps on
        // the run, and a mismatch here lets a posting silently escape the rank view's "still
        // active" check and the diff's "gone" check.
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        int yearMax = DateTime.UtcNow.Year + 1;
        string url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                     $"?api_key=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_range={query.YearMin}-{yearMax}&miles_range=0-{query.MaxMileage}";
        const string body = """
            {
              "listings": [
                {
                  "vin": "19XZE4F52ME000999",
                  "vdp_url": "https://marketcheck.com/listing/1",
                  "price": 19394,
                  "miles": 69599,
                  "build": { "year": 2021, "make": "Honda", "model": "Insight EX", "trim": "EX" }
                }
              ]
            }
            """;
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string> { [url] = body });
        var source = new MarketcheckSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        ListingCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal("Insight", candidate.Model);
        Assert.Equal(["Insight"], result.ModelsCovered);
    }

    [Fact]
    public async Task RunAsync_ApiReturnsTheBaseGasModelForAHybridQuery_CandidateIsRejectedNotUpserted()
    {
        // Marketcheck is free to return a plain "Camry" build.model for a "Camry Hybrid" query;
        // that car is not a hybrid, and upserting it under the canonical "Camry Hybrid" model would
        // let a gas car pass the scorer's target-model filter and get ranked with the hybrid's mpg
        // and insurance figures.
        ListingQuery query = new("Toyota", "Camry Hybrid", YearMin: 2018, "32114", 50, MaxMileage: 100000);
        int yearMax = DateTime.UtcNow.Year + 1;
        string url = "https://mc-api.marketcheck.com/v2/search/car/active" +
                     $"?api_key=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_range={query.YearMin}-{yearMax}&miles_range=0-{query.MaxMileage}";
        const string body = """
            {
              "listings": [
                {
                  "vin": "4T1G11AK5NU000111",
                  "vdp_url": "https://marketcheck.com/listing/2",
                  "price": 21000,
                  "miles": 30000,
                  "build": { "year": 2022, "make": "Toyota", "model": "Camry", "trim": "LE" }
                }
              ]
            }
            """;
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string> { [url] = body });
        var source = new MarketcheckSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Rejections, r => r.Contains("doesn't match the query"));
        Assert.Equal(["Camry Hybrid"], result.ModelsCovered);
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
