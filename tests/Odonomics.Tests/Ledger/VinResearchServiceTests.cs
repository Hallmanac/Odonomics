using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
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

    /// <summary>Pins the 2026-09-28 fix: a vehicle whose history was fetched successfully at some
    /// point (so HistoryRawJson is non-null) but whose most recent attempt hit HTTP 429 must still be
    /// picked up by a plain research run inside the seven-day window, not just a vehicle whose
    /// history was never fetched at all.</summary>
    [Fact]
    public void NeedsRefresh_RecentRecordWithHistoryCouldNotFetchReason_ReturnsTrue()
    {
        var record = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow,
            DecodeRawJson = "",
            ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            HistoryRawJson = "[]",
            HistoryCouldNotFetchReason = "Marketcheck VIN history: HTTP 429",
        };

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

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

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
    public async Task RefreshAsync_SameSellerConsecutiveDayMileageCorrection_AddsANoteAndSkipsTheFlagWithoutDuplicatingOnRerun()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        const string historyJson = """
            [
                {"seller_name":"Daytona Toyota","first_seen_at_date":"2026-09-09T00:00:00.000Z","last_seen_at_date":"2026-09-09T00:00:00.000Z","price":40799,"miles":4703,"vdp_url":"https://example.com/a"},
                {"seller_name":"Daytona Toyota","first_seen_at_date":"2026-09-10T00:00:00.000Z","last_seen_at_date":"2026-09-18T00:00:00.000Z","price":40799,"miles":3852,"vdp_url":"https://example.com/b"}
            ]
            """;
        const string activeSearchJson = """{"num_found":0,"listings":[]}""";
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
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] = historyJson,
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] = activeSearchJson,
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        IReadOnlyList<RedFlag> flags = VinResearchService.RedFlags(result, currentPrice: null);
        Assert.DoesNotContain(flags, f => f.ShortTag == "mileage-drop");

        List<NoteEntity> notes = await db.Notes.Where(n => n.VehicleVin == Vin).ToListAsync(CancellationToken.None);
        NoteEntity note = Assert.Single(notes);
        Assert.Equal("mileage corrected 4,703 to 3,852 at Daytona Toyota on Sep 10", note.Text);

        // A second refresh over the same history must not add a duplicate note.
        await service.RefreshAsync(db, vehicle, refresh: true, CancellationToken.None);
        List<NoteEntity> notesAfterSecondRun = await db.Notes.Where(n => n.VehicleVin == Vin).ToListAsync(CancellationToken.None);
        Assert.Single(notesAfterSecondRun);
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

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.NotNull(result.History.CouldNotFetchReason);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.NotNull(saved.ResearchedAt);
        Assert.Equal(42, saved.CurrentListingDaysOnMarket);
        Assert.Contains("Old Dealer", saved.HistoryRawJson);
    }

    [Fact]
    public async Task RefreshAsync_FreshNhtsaButNoHistory_RetriesOnlyMarketcheck()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        var existingRecord = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow.AddHours(-1),
            DecodeRawJson = "{}",
            ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            OpenRecallCount = 2,
            SafetyOverallRating = 5,
            SafetyRawJson = "{\"OverallRating\":5,\"FrontRating\":null,\"SideRating\":null,\"RolloverRating\":null,\"VehicleDescription\":null,\"ErrorText\":null}",
            HistoryRawJson = null,
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "vin-history-19XZE4F52ME000999.json")),
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "active-search-19XZE4F52ME000999.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.Equal(5, result.Safety.OverallRating);
        Assert.Equal(7, result.History.PriorListings.Count);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.Equal(existingRecord.ResearchedAt, saved.ResearchedAt);
        Assert.Equal(2, saved.OpenRecallCount);
        Assert.NotNull(saved.HistoryRawJson);
    }

    /// <summary>The other half of the 2026-09-28 fix, alongside
    /// <see cref="RefreshAsync_FreshNhtsaButNoHistory_RetriesOnlyMarketcheck"/>: a vehicle whose
    /// history was fetched successfully once (so HistoryRawJson already holds data) but whose most
    /// recent attempt could not be fetched must retry Marketcheck alone on the very next plain run,
    /// inside the seven-day window, without touching NHTSA's already-cached recalls, complaints, or
    /// safety ratings.</summary>
    [Fact]
    public async Task RefreshAsync_HistoryPreviouslyCouldNotFetchWithinCacheWindow_RetriesOnlyMarketcheckAndClearsReason()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        var existingRecord = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow.AddHours(-1),
            DecodeRawJson = "{}",
            ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            OpenRecallCount = 2,
            SafetyOverallRating = 5,
            SafetyRawJson = "{\"OverallRating\":5,\"FrontRating\":null,\"SideRating\":null,\"RolloverRating\":null,\"VehicleDescription\":null,\"ErrorText\":null}",
            HistoryRawJson = "[{\"Dealer\":\"Old Dealer\",\"City\":null,\"State\":null,\"FirstSeen\":null,\"LastSeen\":null,\"Price\":null,\"Mileage\":null,\"Url\":null}]",
            HistoryCouldNotFetchReason = "Marketcheck VIN history: HTTP 429",
            HistoryFetchedAt = DateTimeOffset.UtcNow.AddHours(-1),
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        Assert.True(VinResearchService.NeedsRefresh(existingRecord, refresh: false));

        // Only Marketcheck fixtures registered: if RefreshAsync tried to re-fetch decode, recalls,
        // complaints, or safety ratings too, FixtureHttpMessageHandler would throw "no fixture
        // registered", failing this test before the assertions below even run.
        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "vin-history-19XZE4F52ME000999.json")),
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "active-search-19XZE4F52ME000999.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.Null(result.History.CouldNotFetchReason);
        Assert.Equal(7, result.History.PriorListings.Count);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.Null(saved.HistoryCouldNotFetchReason);
        Assert.NotNull(saved.HistoryFetchedAt);
        Assert.Contains("Holler Classic", saved.HistoryRawJson);
        Assert.Equal(existingRecord.ResearchedAt, saved.ResearchedAt);
        Assert.Equal(2, saved.OpenRecallCount);
    }

    /// <summary>Pins the other half of the 2026-09-28 fix: a Marketcheck VIN-history failure (here,
    /// an HTTP 429 that outlasts <see cref="MarketcheckHistoryClient"/>'s own retries) is persisted as
    /// its own could-not-fetch reason, the same way recalls, complaints, and safety ratings already
    /// record theirs, rather than only leaving HistoryRawJson null with no record of why.</summary>
    [Fact]
    public async Task RefreshAsync_MarketcheckHistoryAlwaysReturns429_PersistsHistoryCouldNotFetchReason()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new NhtsaFixturesMarketcheckAlways429HttpMessageHandler(new Dictionary<string, string>
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
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.NotNull(result.History.CouldNotFetchReason);
        Assert.Contains("429", result.History.CouldNotFetchReason);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.NotNull(saved.HistoryCouldNotFetchReason);
        Assert.Contains("429", saved.HistoryCouldNotFetchReason);
        Assert.Null(saved.HistoryRawJson);
        Assert.NotNull(saved.HistoryFetchedAt);

        // NHTSA came back clean, so the vehicle is still "researched" overall; it's odo research's
        // own summary counting (ResearchCommand.IsPartiallyResearched), not ResearchedAt, that must
        // mark it partial rather than fully researched.
        Assert.NotNull(saved.ResearchedAt);

        // The very next plain run must pick this vehicle back up rather than waiting out the
        // seven-day window.
        Assert.True(VinResearchService.NeedsRefresh(saved, refresh: false));
    }

    /// <summary>A vehicle whose history was fetched successfully before (so the record already holds
    /// prior listings) must not have that history vanish from the caller's result just because this
    /// round's retry failed: recalls, complaints, and safety ratings already keep their last
    /// known-good value alongside a failure's reason, and history must do the same, or a vehicle's
    /// history-based red flags (mileage drop, seller count) would disappear from `odo show` and
    /// `odo research` for as long as the outage lasts, even though the ledger still holds the
    /// history.</summary>
    [Fact]
    public async Task RefreshAsync_HistoryFetchFailsWithPriorHistory_ReturnsCachedHistoryAlongsideReason()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        var existingRecord = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow.AddHours(-1),
            DecodeRawJson = "{}",
            ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            OpenRecallCount = 2,
            SafetyOverallRating = 5,
            SafetyRawJson = "{\"OverallRating\":5,\"FrontRating\":null,\"SideRating\":null,\"RolloverRating\":null,\"VehicleDescription\":null,\"ErrorText\":null}",
            HistoryRawJson = "[{\"Dealer\":\"Old Dealer\",\"City\":null,\"State\":null,\"FirstSeen\":null,\"LastSeen\":null,\"Price\":null,\"Mileage\":null,\"Url\":null}]",
            CurrentListingDaysOnMarket = 42,
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        // No NHTSA fixtures registered: the record is within its cache window and none of its NHTSA
        // pieces previously failed, so RefreshAsync must not call NHTSA at all here. If it did, this
        // handler would throw "no fixture registered", failing the test before the assertions below.
        var handler = new NhtsaFixturesMarketcheckAlways429HttpMessageHandler(new Dictionary<string, string>());
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.NotNull(result.History.CouldNotFetchReason);
        Assert.Contains("429", result.History.CouldNotFetchReason);
        VinHistoryListing carried = Assert.Single(result.History.PriorListings);
        Assert.Equal("Old Dealer", carried.Dealer);
        Assert.Equal(42, result.History.CurrentListingDaysOnMarket);
    }

    /// <summary>Answers every NHTSA URL with its registered fixture and every Marketcheck URL with
    /// HTTP 429 (Retry-After: 0, so the test never waits out a real backoff), so a test can prove
    /// NHTSA succeeds while Marketcheck's own retries are exhausted.</summary>
    private sealed class NhtsaFixturesMarketcheckAlways429HttpMessageHandler(IReadOnlyDictionary<string, string> nhtsaResponsesByUrl) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? throw new InvalidOperationException("request has no URL");
            if (url.StartsWith("https://mc-api.marketcheck.com", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("") };
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(response);
            }

            if (!nhtsaResponsesByUrl.TryGetValue(url, out string? body))
            {
                throw new InvalidOperationException($"no fixture registered for {url}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task RefreshAsync_SafetyRatingsCallFails_StillPersistsRecallsAndComplaints()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        string htmlError = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "akamai-error.html"));
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Vin}?format=json"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "decode-1HGCM82633A004352.json")),
            ["https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "recalls-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "complaints-honda-insight-2020.json")),
            ["https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight"] = htmlError,
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "vin-history-19XZE4F52ME000999.json")),
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "active-search-19XZE4F52ME000999.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.Null(result.Recalls.CouldNotFetchReason);
        Assert.NotEmpty(result.Recalls.Entries);
        Assert.Null(result.Complaints.CouldNotFetchReason);
        Assert.Equal(31, result.Complaints.Count);
        Assert.NotNull(result.Safety.CouldNotFetchReason);
        Assert.Contains("NHTSA safety ratings", result.Safety.CouldNotFetchReason);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.True(saved.OpenRecallCount > 0);
        Assert.Equal(31, saved.ComplaintCount);
        Assert.Null(saved.RecallsCouldNotFetchReason);
        Assert.Null(saved.ComplaintsCouldNotFetchReason);
        Assert.NotNull(saved.SafetyCouldNotFetchReason);
        Assert.NotNull(saved.SafetyFetchedAt);
        Assert.Null(saved.SafetyRawJson);
    }

    [Fact]
    public async Task RefreshAsync_PreviousSafetyCouldNotFetch_RetriesOnlySafety()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        var existingRecord = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow.AddHours(-1),
            DecodeRawJson = "{}",
            ResearchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            OpenRecallCount = 2,
            RecallsRawJson = "[]",
            ComplaintCount = 4,
            SafetyCouldNotFetchReason = "NHTSA safety ratings could not be fetched, HTTP 200 with an HTML error page",
            SafetyFetchedAt = DateTimeOffset.UtcNow.AddHours(-1),
            HistoryRawJson = "[]",
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
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

        // No recalls or complaints fixture is registered: if RefreshAsync tried to re-fetch either
        // one, FixtureHttpMessageHandler would throw "no fixture registered", failing this test.
        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.Null(result.Safety.CouldNotFetchReason);
        Assert.Equal(5, result.Safety.OverallRating);
        Assert.Empty(result.Recalls.Entries);
        Assert.Equal(4, result.Complaints.Count);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.Null(saved.SafetyCouldNotFetchReason);
        Assert.Equal(5, saved.SafetyOverallRating);
        Assert.Equal(2, saved.OpenRecallCount);
        Assert.Equal(4, saved.ComplaintCount);
    }

    [Fact]
    public async Task RefreshAsync_RecallsCallFailsOnStaleRecordWithCachedRecalls_ReturnedResultKeepsCachedEntries()
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
            OpenRecallCount = 2,
            RecallsRawJson = "[{\"CampaignNumber\":\"21V001\",\"Component\":\"AIR BAGS\",\"Summary\":\"s\",\"Consequence\":\"c\",\"Remedy\":\"Remedy is not yet available. Please check back for updates.\",\"ReportReceivedDate\":\"2021-01-01\"},{\"CampaignNumber\":\"21V002\",\"Component\":\"FUEL SYSTEM\",\"Summary\":\"s\",\"Consequence\":\"c\",\"Remedy\":\"r\",\"ReportReceivedDate\":\"2021-01-02\"}]",
            ComplaintCount = 4,
            SafetyOverallRating = 3,
            SafetyRawJson = "{\"OverallRating\":3,\"FrontRating\":null,\"SideRating\":null,\"RolloverRating\":null,\"VehicleDescription\":null,\"ErrorText\":null}",
            HistoryRawJson = "[]",
        };
        db.Vehicles.Add(vehicle);
        db.VinRecords.Add(existingRecord);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        string htmlError = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "akamai-error.html"));
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Vin}?format=json"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "decode-1HGCM82633A004352.json")),
            ["https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020"] = htmlError,
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

        // Recalls fails on both attempts (the fixture always answers with the Akamai HTML page), so
        // the persisted OpenRecallCount/RecallsRawJson stay untouched (RefreshAsync_...StillPersists
        // already covers that). What this test proves is the *returned* result: a caller like
        // ShowRenderer or RedFlags must still see the two cached recalls, with the failure reason
        // layered on top, rather than an empty list that would hide the real no-remedy red flag
        // carried by one of them.
        VinResearchResult result = await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        Assert.NotNull(result.Recalls.CouldNotFetchReason);
        Assert.Equal(2, result.Recalls.Entries.Count);
        IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlags(result, currentPrice: null);
        Assert.Contains(redFlags, f => f.ShortTag == "no-remedy-recall");
    }

    [Fact]
    public async Task RefreshAsync_FirstResearchAllNhtsaPiecesFail_DoesNotStampResearchedAt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = Vehicle();
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);

        string fixtureRoot = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures");
        string htmlError = await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "akamai-error.html"));
        var handler = new FixtureHttpMessageHandler(new Dictionary<string, string>
        {
            [$"https://vpic.nhtsa.dot.gov/api/vehicles/DecodeVinValues/{Vin}?format=json"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "nhtsa", "decode-1HGCM82633A004352.json")),
            ["https://api.nhtsa.gov/recalls/recallsByVehicle?make=Honda&model=Insight&modelYear=2020"] = htmlError,
            ["https://api.nhtsa.gov/complaints/complaintsByVehicle?make=Honda&model=Insight&modelYear=2020"] = htmlError,
            ["https://api.nhtsa.gov/SafetyRatings/modelyear/2020/make/Honda/model/Insight"] = htmlError,
            ["https://mc-api.marketcheck.com/v2/history/car/1HGCM82633A004352?api_key=test-key"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "vin-history-19XZE4F52ME000999.json")),
            ["https://mc-api.marketcheck.com/v2/search/car/active?api_key=test-key&vin=1HGCM82633A004352"] =
                await File.ReadAllTextAsync(Path.Combine(fixtureRoot, "marketcheck", "active-search-19XZE4F52ME000999.json")),
        });
        var http = new HttpClient(handler);
        var service = new VinResearchService(new NhtsaClient(http), new MarketcheckHistoryClient("test-key", http));

        await service.RefreshAsync(db, vehicle, refresh: false, CancellationToken.None);

        VinRecordEntity saved = await db.VinRecords.FindAsync([Vin]) ?? throw new InvalidOperationException();
        Assert.Null(saved.ResearchedAt);

        // Since ResearchedAt is still null, the next research run still picks this vehicle up
        // rather than a rank-only reader mistaking it for "researched, clean".
        Assert.True(VinResearchService.NeedsRefresh(saved, refresh: false));
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
    public void RedFlagsForCached_RecordWithRecallAndNoRemedyAvailable_FlagsIt()
    {
        var record = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow,
            DecodeRawJson = "{}",
            OpenRecallCount = 1,
            RecallsRawJson = "[{\"CampaignNumber\":\"21V001\",\"Component\":\"AIR BAGS\",\"Summary\":\"s\",\"Consequence\":\"c\",\"Remedy\":\"\",\"ReportReceivedDate\":\"2021-01-01\"}]",
        };

        IReadOnlyList<RedFlag> flags = VinResearchService.RedFlagsForCached(record, currentPrice: null);

        Assert.Contains(flags, f => f.ShortTag == "no-remedy-recall");
    }

    [Fact]
    public void RedFlagsForCached_RecordWithRecallAndRemedyAvailable_NoFlag()
    {
        var record = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow,
            DecodeRawJson = "{}",
            OpenRecallCount = 1,
            RecallsRawJson = "[{\"CampaignNumber\":\"21V001\",\"Component\":\"AIR BAGS\",\"Summary\":\"s\",\"Consequence\":\"c\",\"Remedy\":\"dealers will fix it\",\"ReportReceivedDate\":\"2021-01-01\"}]",
        };

        IReadOnlyList<RedFlag> flags = VinResearchService.RedFlagsForCached(record, currentPrice: null);

        Assert.DoesNotContain(flags, f => f.ShortTag == "no-remedy-recall");
    }

    [Fact]
    public void RedFlagsForCached_HistoryWithASalvageSeller_FlagsItForRank()
    {
        var record = new VinRecordEntity
        {
            Vin = Vin,
            DecodedAt = DateTimeOffset.UtcNow,
            DecodeRawJson = "{}",
            HistoryRawJson = JsonSerializer.Serialize(new List<VinHistoryListing>
            {
                new("Erepairables.com", null, null, new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero), null, 83630, null),
                new("Liz Auto Sales Llc", null, null, new DateTimeOffset(2026, 8, 18, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), 15995m, 83682, null),
            }),
        };

        IReadOnlyList<RedFlag> flags = VinResearchService.RedFlagsForCached(record, currentPrice: 17289m);

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("salvage-seller", flag.ShortTag);
    }
}
