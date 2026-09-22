using Odonomics.Nhtsa;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Nhtsa;

/// <summary>Contract tests against recorded NHTSA fixtures (tests/Odonomics.Tests/fixtures/nhtsa/),
/// fetched once from the live vPIC, recalls, and complaints endpoints. No live network call runs
/// during the test suite; see FixtureHttpMessageHandler.</summary>
public class NhtsaClientTests
{
    private static string FixturePath(string fileName) => Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "nhtsa", fileName);

    private static NhtsaClient BuildClient(Dictionary<string, string> responsesByUrl)
    {
        var handler = new FixtureHttpMessageHandler(responsesByUrl);
        var http = new HttpClient(handler);
        return new NhtsaClient(http);
    }

    [Fact]
    public async Task DecodeVinAsync_RecordedFixture_ParsesFields()
    {
        const string vin = "1HGCM82633A004352";
        string url = $"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{vin}?format=json";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("decode-1HGCM82633A004352.json")),
        });

        VinDecodeResult result = await client.DecodeVinAsync(vin, CancellationToken.None);

        Assert.Equal(vin, result.Vin);
        Assert.Equal(2003, result.Year);
        Assert.Equal("HONDA", result.Make);
        Assert.Equal("Accord", result.Model);
        Assert.Equal("EX-V6", result.Trim);
        Assert.Equal("Coupe", result.BodyClass);
        Assert.Contains("decoded clean", result.ErrorText);
    }

    [Fact]
    public async Task GetRecallsAsync_RecordedFixtureWithRecalls_ParsesEveryCampaign()
    {
        string url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("recalls-honda-insight-2020.json")),
        });

        IReadOnlyList<RecallEntry> recalls = await client.GetRecallsAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.NotEmpty(recalls);
        Assert.All(recalls, r => Assert.False(string.IsNullOrWhiteSpace(r.CampaignNumber)));
        Assert.Contains(recalls, r => r.Component.Contains("DC/DC CONVERTER"));
    }

    [Fact]
    public async Task GetRecallsAsync_RecordedFixtureWithNoRecalls_ReturnsEmpty()
    {
        string url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=Toyota&model=Prius&modelYear=2020";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("recalls-toyota-prius-2020.json")),
        });

        IReadOnlyList<RecallEntry> recalls = await client.GetRecallsAsync("Toyota", "Prius", 2020, CancellationToken.None);

        Assert.Empty(recalls);
    }

    [Fact]
    public async Task GetComplaintCountAsync_RecordedFixture_ReturnsCount()
    {
        string url = "https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("complaints-honda-insight-2020.json")),
        });

        int count = await client.GetComplaintCountAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.Equal(31, count);
    }

    [Fact]
    public async Task GetComplaintCountAsync_RecordedFixtureWithZeroComplaints_ReturnsZero()
    {
        // NHTSA's complaintsByVehicle endpoint carries the same quirk as recallsByVehicle: a
        // brand-new model year with zero complaints on file comes back as HTTP 400 with a perfectly
        // valid "count":0 body. This fixture was recorded from exactly that case (2026 Toyota
        // Corolla Hybrid); GetComplaintCountAsync must read the body rather than throw on the
        // status code, the same way GetRecallsAsync already does.
        string url = "https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Toyota&model=Corolla Hybrid&modelYear=2026";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("complaints-toyota-corolla-hybrid-2026-zero.json")),
        });

        int count = await client.GetComplaintCountAsync("Toyota", "Corolla Hybrid", 2026, CancellationToken.None);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetSafetyRatingsAsync_RecordedFixture_ParsesEveryCategory()
    {
        string lookupUrl = "https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight";
        string detailUrl = "https://api.nhtsa.gov/SafetyRatings/VehicleId/14485";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [lookupUrl] = await File.ReadAllTextAsync(FixturePath("safety-ratings-lookup-honda-insight-2020.json")),
            [detailUrl] = await File.ReadAllTextAsync(FixturePath("safety-ratings-detail-14485.json")),
        });

        SafetyRatingsResult result = await client.GetSafetyRatingsAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.Equal(5, result.OverallRating);
        Assert.Equal(5, result.FrontRating);
        Assert.Equal(5, result.SideRating);
        Assert.Equal(5, result.RolloverRating);
        Assert.Null(result.ErrorText);
        Assert.Contains("INSIGHT", result.VehicleDescription);
    }

    [Fact]
    public async Task GetSafetyRatingsAsync_RecordedFixtureWithNoVehicleOnFile_ReturnsErrorTextNoStars()
    {
        string lookupUrl = "https://api.nhtsa.gov/SafetyRatings/modelyear/1988/make/Yugo/model/GV";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [lookupUrl] = await File.ReadAllTextAsync(FixturePath("safety-ratings-lookup-no-results.json")),
        });

        SafetyRatingsResult result = await client.GetSafetyRatingsAsync("Yugo", "GV", 1988, CancellationToken.None);

        Assert.Null(result.OverallRating);
        Assert.NotNull(result.ErrorText);
    }
}
