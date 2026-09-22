using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;
using Odonomics.Marketcheck;
using Odonomics.Nhtsa;
using Odonomics.Tests.TestSupport;

namespace Odonomics.Tests.Ledger;

public class VinResearchServiceTests
{
    private const string Vin = "1HGCM82633A004352";

    private static VehicleEntity Vehicle() => new()
    {
        Vin = Vin,
        Year = 2020,
        Make = "Honda",
        Model = "Insight",
        Mileage = 40000,
        FirstSeen = DateTimeOffset.UtcNow,
        LastSeen = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void NeedsRefresh_NoRecord_ReturnsTrue()
    {
        Assert.True(VinResearchService.NeedsRefresh(null, refresh: false));
    }

    [Fact]
    public void NeedsRefresh_RecentRecord_ReturnsFalse()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "", ResearchedAt = DateTimeOffset.UtcNow.AddDays(-1), HistoryRawJson = "[]" };

        Assert.False(VinResearchService.NeedsRefresh(record, refresh: false));
    }

    [Fact]
    public void NeedsRefresh_StaleRecord_ReturnsTrue()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "", ResearchedAt = DateTimeOffset.UtcNow.AddDays(-8), HistoryRawJson = "[]" };

        Assert.True(VinResearchService.NeedsRefresh(record, refresh: false));
    }

    [Fact]
    public void NeedsRefresh_RefreshFlagOnRecentRecord_ReturnsTrue()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "", ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1), HistoryRawJson = "[]" };

        Assert.True(VinResearchService.NeedsRefresh(record, refresh: true));
    }

    [Fact]
    public void NeedsRefresh_RecentRecordWithoutHistory_ReturnsTrue()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "", ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1), HistoryRawJson = null };

        Assert.True(VinResearchService.NeedsRefresh(record, refresh: false));
    }

    [Fact]
    public async Task RefreshAsync_RecordedFixtures_PersistsDecodeSafetyAndHistory()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Vin}?format=json"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "decode-1HGCM82633A004352.json")),
            ["https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "recalls-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "complaints-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "safety-ratings-lookup-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/SafetyRatings/VehicleId/14485"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "safety-ratings-detail-14485.json")),
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "vin-history-19XZE4F52ME000999.json")),
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "active-search-19XZE4F52ME000999.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, CancellationToken.None);

        Assert.Equal(5, result.Safety.OverallRating);
        Assert.Equal(88, result.History.CurrentListingDaysOnMarket);
        Assert.Equal(7, result.History.PriorListings.Count);

        VinRecordEntity? saved = await db.VinRecords.FindAsync([Vin]);
        Assert.NotNull(saved);
        Assert.NotNull(saved.ResearchedAt);
        Assert.Equal(5, saved.SafetyOverallRating);
        Assert.Equal(88, saved.CurrentListingDaysOnMarket);
        Assert.NotNull(saved.HistoryRawJson);
    }

    [Fact]
    public async Task RefreshAsync_MarketcheckHasNoKey_KeepsPreviousHistoryButStillStampsResearchedAt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        var existingRecord = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow.AddDays(-10),
            DecodeRawJson = "{}",
            ResearchedAt = DateTimeOffset.UtcNow.AddDays(-10),
            HistoryRawJson = "[{\"Dealer\":\"Old Dealer\",\"City\":null,\"State\":null,\"FirstSeen\":null,\"LastSeen\":null,\"Price\":null,\"Mileage\":null,\"Url\":null}]",
            CurrentListingDaysOnMarket = 42,
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Vin}?format=json"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "decode-1HGCM82633A004352.json")),
            ["https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "recalls-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "complaints-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "safety-ratings-lookup-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/SafetyRatings/VehicleId/14485"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "safety-ratings-detail-14485.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient(apiKey: null, http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, CancellationToken.None);

        Assert.NotNull(result.History.CouldNotFetchReason);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.NotNull(saved.ResearchedAt);
        Assert.Equal(42, saved.CurrentListingDaysOnMarket);
        Assert.Contains("Old Dealer", saved.HistoryRawJson);
    }

    [Fact]
    public void FromCached_NeverResearched_ReturnsPlaceholderNotesWithoutThrowing()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "{}" };

        VinResearchResult result = VinResearchService.FromCached(record);

        Assert.NotNull(result.Safety.ErrorText);
        Assert.NotNull(result.History.CouldNotFetchReason);
        Assert.Empty(result.History.PriorListings);
    }

    [Fact]
    public void RedFlagsForCached_RecordWithOpenRecall_IncludesRecallFlag()
    {
        var record = new VinRecordEntity { Vin = Vin, DecodedAt = DateTimeOffset.UtcNow, DecodeRawJson = "{}", OpenRecallCount = 1 };

        IReadOnlyList<string> flags = VinResearchService.RedFlagsForCached(record, currentPrice: null);

        Assert.Contains(flags, f => f.Contains("open NHTSA recall"));
    }
}
