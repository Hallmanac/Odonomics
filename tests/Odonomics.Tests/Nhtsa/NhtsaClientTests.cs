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
}
