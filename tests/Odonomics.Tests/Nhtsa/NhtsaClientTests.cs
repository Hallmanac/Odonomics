using System.Net;
using Odonomics.Nhtsa;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Nhtsa;

/// <summary>Contract tests against recorded NHTSA fixtures (tests/Odonomics.Tests/fixtures/nhtsa/),
/// fetched once from the live vPIC, recalls, and complaints endpoints, plus a recorded Akamai HTML
/// error page (akamai-error.html) proving the retry-then-degrade behavior described in
/// NhtsaClient.FetchAsync. No live network call runs during the test suite; see
/// FixtureHttpMessageHandler and SequencedHttpMessageHandler.</summary>
public class NhtsaClientTests
{
    private static string FixturePath(string fileName) => Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "nhtsa", fileName);

    private static NhtsaClient BuildClient(Dictionary<string, string> responsesByUrl)
    {
        var handler = new FixtureHttpMessageHandler(responsesByUrl);
        var http = new HttpClient(handler);
        return new NhtsaClient(http);
    }

    private static NhtsaClient BuildClient(Dictionary<string, Queue<(HttpStatusCode Status, string Body)>> sequencedResponsesByUrl)
    {
        var handler = new SequencedHttpMessageHandler(sequencedResponsesByUrl);
        var http = new HttpClient(handler);
        return new NhtsaClient(http);
    }

    private static Queue<(HttpStatusCode Status, string Body)> Sequence(params (HttpStatusCode Status, string Body)[] responses) => new(responses);

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

        RecallsResult result = await client.GetRecallsAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.NotEmpty(result.Entries);
        Assert.All(result.Entries, r => Assert.False(string.IsNullOrWhiteSpace(r.CampaignNumber)));
        Assert.Contains(result.Entries, r => r.Component.Contains("DC/DC CONVERTER"));
    }

    [Fact]
    public async Task GetRecallsAsync_RecordedFixtureWithNoRecalls_ReturnsEmpty()
    {
        string url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=Toyota&model=Prius&modelYear=2020";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("recalls-toyota-prius-2020.json")),
        });

        RecallsResult result = await client.GetRecallsAsync("Toyota", "Prius", 2020, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetRecallsAsync_FirstAttemptHtmlErrorPage_RetriesAndSucceeds()
    {
        string url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=Toyota&model=Prius&modelYear=2026";
        string htmlError = await File.ReadAllTextAsync(FixturePath("akamai-error.html"));
        string goodBody = await File.ReadAllTextAsync(FixturePath("recalls-toyota-prius-2020.json"));
        NhtsaClient client = BuildClient(new Dictionary<string, Queue<(HttpStatusCode, string)>>
        {
            [url] = Sequence((HttpStatusCode.OK, htmlError), (HttpStatusCode.OK, goodBody)),
        });

        RecallsResult result = await client.GetRecallsAsync("Toyota", "Prius", 2026, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task GetRecallsAsync_BothAttemptsHtmlErrorPage_ReturnsCouldNotFetchReasonNamingSourceAndStatus()
    {
        string url = "https://api.nhtsa.gov/recalls/recallsByVehicle?make=Toyota&model=Prius&modelYear=2026";
        string htmlError = await File.ReadAllTextAsync(FixturePath("akamai-error.html"));
        NhtsaClient client = BuildClient(new Dictionary<string, Queue<(HttpStatusCode, string)>>
        {
            [url] = Sequence((HttpStatusCode.OK, htmlError), (HttpStatusCode.OK, htmlError)),
        });

        RecallsResult result = await client.GetRecallsAsync("Toyota", "Prius", 2026, CancellationToken.None);

        Assert.Empty(result.Entries);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("NHTSA recalls", result.CouldNotFetchReason);
        Assert.Contains("HTTP 200", result.CouldNotFetchReason);
        Assert.Contains("HTML error page", result.CouldNotFetchReason);
        Assert.DoesNotContain("<HTML>", result.CouldNotFetchReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetComplaintCountAsync_RecordedFixture_ReturnsCount()
    {
        string url = "https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020";
        NhtsaClient client = BuildClient(new Dictionary<string, string>
        {
            [url] = await File.ReadAllTextAsync(FixturePath("complaints-honda-insight-2020.json")),
        });

        ComplaintsResult result = await client.GetComplaintCountAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Equal(31, result.Count);
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

        ComplaintsResult result = await client.GetComplaintCountAsync("Toyota", "Corolla Hybrid", 2026, CancellationToken.None);

        Assert.Null(result.CouldNotFetchReason);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task GetComplaintCountAsync_BothAttemptsHtmlErrorPage_ReturnsCouldNotFetchReasonNamingSourceAndStatus()
    {
        string url = "https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Toyota&model=Prius&modelYear=2026";
        string htmlError = await File.ReadAllTextAsync(FixturePath("akamai-error.html"));
        NhtsaClient client = BuildClient(new Dictionary<string, Queue<(HttpStatusCode, string)>>
        {
            [url] = Sequence((HttpStatusCode.OK, htmlError), (HttpStatusCode.OK, htmlError)),
        });

        ComplaintsResult result = await client.GetComplaintCountAsync("Toyota", "Prius", 2026, CancellationToken.None);

        Assert.Equal(0, result.Count);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("NHTSA complaints", result.CouldNotFetchReason);
        Assert.Contains("HTTP 200", result.CouldNotFetchReason);
        Assert.Contains("HTML error page", result.CouldNotFetchReason);
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
        Assert.Null(result.CouldNotFetchReason);
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
        Assert.Null(result.CouldNotFetchReason);
    }

    [Fact]
    public async Task GetSafetyRatingsAsync_LookupBothAttemptsHtmlErrorPage_ReturnsCouldNotFetchReasonNamingSourceAndStatus()
    {
        string lookupUrl = "https://api.nhtsa.gov/SafetyRatings/modelyear/2026/make/Toyota/model/Prius";
        string htmlError = await File.ReadAllTextAsync(FixturePath("akamai-error.html"));
        NhtsaClient client = BuildClient(new Dictionary<string, Queue<(HttpStatusCode, string)>>
        {
            [lookupUrl] = Sequence((HttpStatusCode.OK, htmlError), (HttpStatusCode.OK, htmlError)),
        });

        SafetyRatingsResult result = await client.GetSafetyRatingsAsync("Toyota", "Prius", 2026, CancellationToken.None);

        Assert.Null(result.OverallRating);
        Assert.Null(result.ErrorText);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("NHTSA safety ratings", result.CouldNotFetchReason);
        Assert.Contains("HTTP 200", result.CouldNotFetchReason);
        Assert.Contains("HTML error page", result.CouldNotFetchReason);
    }

    [Fact]
    public async Task GetSafetyRatingsAsync_DetailBothAttemptsHtmlErrorPage_ReturnsCouldNotFetchReasonWithVehicleDescription()
    {
        string lookupUrl = "https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight";
        string detailUrl = "https://api.nhtsa.gov/SafetyRatings/VehicleId/14485";
        string htmlError = await File.ReadAllTextAsync(FixturePath("akamai-error.html"));
        NhtsaClient client = BuildClient(new Dictionary<string, Queue<(HttpStatusCode, string)>>
        {
            [lookupUrl] = Sequence((HttpStatusCode.OK, await File.ReadAllTextAsync(FixturePath("safety-ratings-lookup-honda-insight-2020.json")))),
            [detailUrl] = Sequence((HttpStatusCode.OK, htmlError), (HttpStatusCode.OK, htmlError)),
        });

        SafetyRatingsResult result = await client.GetSafetyRatingsAsync("Honda", "Insight", 2020, CancellationToken.None);

        Assert.Null(result.OverallRating);
        Assert.NotNull(result.CouldNotFetchReason);
        Assert.Contains("NHTSA safety ratings", result.CouldNotFetchReason);
        Assert.Contains("INSIGHT", result.VehicleDescription);
    }
}
