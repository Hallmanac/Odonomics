using Odonomics.Ledger;
using Odonomics.Sources;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Sources;

/// <summary>Contract test against a real auto.dev response recorded by the spike
/// (tests/Odonomics.Tests/fixtures/sources/auto.dev/), so the parser is proven against the
/// API's actual shape rather than a hand-written stub.</summary>
public class AutoDevSourceTests
{
    private static string FixturePath => Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "sources", "auto.dev", "honda-insight.json");

    [Fact]
    public async Task RunAsync_NoApiKey_ReportsCouldNotRun()
    {
        var source = new AutoDevSource(apiKey: null, new HttpClient());

        SourceResult result = await source.RunAsync([new ListingQuery("Honda", "Insight", 2019, "32114", 50, 100000)], CancellationToken.None);

        Assert.True(result.CouldNotRun);
        Assert.Contains("AutoDev:ApiKey", result.CouldNotRunReason);
    }

    [Fact]
    public async Task RunAsync_RecordedFixture_ParsesEveryInRangeCandidateWithAVin()
    {
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        string url = $"https://auto.dev/api/listings?apikey=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}";
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath),
        });
        var source = new AutoDevSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.False(result.CouldNotRun);
        Assert.Equal(3, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Vin == "19XZE4F52ME000999" && c.Price == 19393m && c.Mileage == 69599);
        Assert.All(result.Candidates, c => Assert.False(string.IsNullOrWhiteSpace(c.Url)));
        Assert.All(result.Candidates, c => Assert.Equal("auto.dev", c.Source));
        Assert.Equal(["Insight"], result.ModelsCovered);
    }

    [Fact]
    public async Task RunAsync_QueryFails_ModelIsNotReportedCovered()
    {
        // A query that comes back as an error (a bad key, a rate limit, a timeout) must not be
        // treated the same as one that ran and legitimately found nothing: the caller uses
        // ModelsCovered to decide which of this run's postings it can trust as "still active" or
        // "gone", and a failed query never actually confirmed either.
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        var handler = new StatusCodeHttpMessageHandler(System.Net.HttpStatusCode.TooManyRequests);
        var source = new AutoDevSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.False(result.CouldNotRun);
        Assert.Empty(result.ModelsCovered);
        Assert.Contains(result.Rejections, r => r.Contains("HTTP 429"));
    }

    [Fact]
    public async Task RunAsync_ApiReturnsADifferentModelStringThanQueried_CandidateModelStaysCanonical()
    {
        // The API is free to return its own "model" field ("Insight" for a query on "Insight EX",
        // or vice versa); the candidate must carry the model this run actually queried for, since
        // that's the model half of the "source:model" token RunSources.Key stamps on the run, and a
        // mismatch here is exactly what lets a posting silently escape both the rank view's
        // "still active" check and the diff's "gone" check.
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 100000);
        string url = $"https://auto.dev/api/listings?apikey=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}";
        const string body = """
            {
              "records": [
                {
                  "vin": "19XZE4F52ME000999",
                  "vdpUrl": "https://auto.dev/listing/1",
                  "year": 2021,
                  "make": "Honda",
                  "model": "Insight EX",
                  "trim": "EX",
                  "priceUnformatted": 19393,
                  "mileageUnformatted": 69599
                }
              ]
            }
            """;
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string> { [url] = body });
        var source = new AutoDevSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        ListingCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal("Insight", candidate.Model);
        Assert.Equal(["Insight"], result.ModelsCovered);
    }

    [Fact]
    public async Task RunAsync_ApiReturnsTheBaseGasModelForAHybridQuery_CandidateIsRejectedNotUpserted()
    {
        // auto.dev is free to return a plain "Camry" record for a "Camry Hybrid" query; that car
        // is not a hybrid, and upserting it under the canonical "Camry Hybrid" model would let a
        // gas car pass the scorer's target-model filter and get ranked with the hybrid's mpg and
        // insurance figures.
        ListingQuery query = new("Toyota", "Camry Hybrid", YearMin: 2018, "32114", 50, MaxMileage: 100000);
        string url = $"https://auto.dev/api/listings?apikey=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}";
        const string body = """
            {
              "records": [
                {
                  "vin": "4T1G11AK5NU000111",
                  "vdpUrl": "https://auto.dev/listing/2",
                  "year": 2022,
                  "make": "Toyota",
                  "model": "Camry",
                  "trim": "LE",
                  "priceUnformatted": 21000,
                  "mileageUnformatted": 30000
                }
              ]
            }
            """;
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string> { [url] = body });
        var source = new AutoDevSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.Empty(result.Candidates);
        Assert.Contains(result.Rejections, r => r.Contains("doesn't match the query"));
        Assert.Equal(["Camry Hybrid"], result.ModelsCovered);
    }

    [Fact]
    public async Task RunAsync_CandidateOutsideMileageRange_IsRejectedNotUpserted()
    {
        // The fixture's third record is a 2022 with 84,595 miles; a max-mileage query of 50,000
        // must reject it while keeping the ones that do fit.
        ListingQuery query = new("Honda", "Insight", YearMin: 2019, "32114", 50, MaxMileage: 50000);
        string url = $"https://auto.dev/api/listings?apikey=test-key&zip={query.Zip}&radius={query.RadiusMiles}" +
                     $"&make={query.Make}&model={query.Model}&year_min={query.YearMin}&mileage_max={query.MaxMileage}";
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath),
        });
        var source = new AutoDevSource("test-key", new HttpClient(handler));

        SourceResult result = await source.RunAsync([query], CancellationToken.None);

        Assert.DoesNotContain(result.Candidates, c => c.Vin == "19XZE4F52ME000999"); // 69,599 miles
        Assert.Contains(result.Candidates, c => c.Vin == "19XZE4F95ME001552"); // 32,500 miles
        Assert.NotEmpty(result.Rejections);
    }
}
