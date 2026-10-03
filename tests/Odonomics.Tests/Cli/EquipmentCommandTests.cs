using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Tests.Ledger;

namespace Odonomics.Tests.Cli;

/// <summary>`odo equipment set`: a window-sticker reading made by hand is stored with the manual-sticker source,
/// ranks like a walked sticker, and is replaced by a later walked sticker or a later manual entry.</summary>
public class EquipmentCommandTests
{
    private const string Vin = "JTDBCMFE0S3083487";
    private const string PdfSource = "Toyota sticker PDF 2026-10-03";

    private static readonly DateTimeOffset RunTime = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static readonly Scenario DaughterScenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static readonly FactoryTrimTable PresentTable = FactoryTrimTable.Parse("""
        { "entries": [
          { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2025, "trim": "LE",
            "smartKeyEntry": "present", "pushButtonStart": "present", "source": "test sheet" }
        ] }
        """);

    private static async Task<VehicleEntity> AddCorollaAsync(OdonomicsDbContext db)
    {
        var vehicle = new VehicleEntity
        {
            Vin = Vin,
            Year = 2025,
            Make = "Toyota",
            Model = "Corolla Hybrid",
            Trim = "LE",
            Mileage = 12000,
            FirstSeen = RunTime,
            LastSeen = RunTime,
            Postings =
            [
                new PostingEntity
                {
                    VehicleVin = Vin,
                    Source = "carvana",
                    Url = "https://example.com/jtdbcmfe0s3083487",
                    FirstSeen = RunTime,
                    LastSeen = RunTime,
                    PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 21990m, ObservedAt = RunTime }],
                },
            ],
        };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(CancellationToken.None);
        return vehicle;
    }

    private static Score ScoreOf(VehicleEntity vehicle, FactoryTrimTable? table = null) =>
        Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles, table), DaughterScenario);

    [Fact]
    public async Task SetAsync_GivenStatuses_StoresEachWithTheManualStickerSourceAndTheText()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await AddCorollaAsync(db);

        bool found = await EquipmentCommand.SetAsync(db, Vin, EquipmentStatus.Absent, EquipmentStatus.Absent, EquipmentStatus.Present, "  " + PdfSource + "  ", CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        VehicleEquipment equipment = reread.Vehicles.Single().StoredEquipment;
        Assert.True(found);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.ManualSticker, PdfSource), equipment.SmartKeyEntry);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.ManualSticker, PdfSource), equipment.PushButtonStart);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.ManualSticker, PdfSource), equipment.KeylessFobEntry);
        Assert.Equal("absent (sticker (manual): Toyota sticker PDF 2026-10-03)", equipment.PushButtonStart.Text);
    }

    [Fact]
    public async Task SetAsync_OnlyOneStatusGiven_LeavesTheOthersAsTheyWere()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = await AddCorollaAsync(db);
        vehicle.SmartKeyEntry = EquipmentStatus.Present;
        vehicle.SmartKeyEntrySource = EquipmentSource.WindowSticker;
        await db.SaveChangesAsync(CancellationToken.None);

        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);

        VehicleEquipment equipment = db.Vehicles.Single().StoredEquipment;
        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.WindowSticker), equipment.SmartKeyEntry);
        Assert.Equal(EquipmentStatus.Absent, equipment.PushButtonStart.Status);
        Assert.Equal(EquipmentFact.Unknown, equipment.KeylessFobEntry);
    }

    [Fact]
    public async Task SetAsync_VinNotInTheLedger_ReturnsFalse()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();

        bool found = await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);

        Assert.False(found);
    }

    [Fact]
    public async Task SetAsync_AfterAWalkedSticker_ReplacesItAndDropsNothingElse()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = await AddCorollaAsync(db);
        vehicle.PushButtonStart = EquipmentStatus.Present;
        vehicle.PushButtonStartSource = EquipmentSource.WindowSticker;
        await db.SaveChangesAsync(CancellationToken.None);

        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);

        Assert.Equal(new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.ManualSticker, PdfSource), db.Vehicles.Single().StoredEquipment.PushButtonStart);
    }

    [Fact]
    public async Task SetAsync_ASecondManualEntry_ReplacesTheStatusAndTheText()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await AddCorollaAsync(db);

        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);
        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Present, null, "dealer photo of the sticker", CancellationToken.None);

        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.ManualSticker, "dealer photo of the sticker"), db.Vehicles.Single().StoredEquipment.PushButtonStart);
    }

    [Fact]
    public async Task UpsertAsync_WalkedStickerAfterAManualEntry_ReplacesItAndClearsTheText()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        var run = new RunEntity { Command = "walk", Sources = "carvana", StartedAt = RunTime };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        var candidate = new ListingCandidate
        {
            Vin = Vin,
            Source = "carvana",
            Url = "https://example.com/jtdbcmfe0s3083487",
            Year = 2025,
            Make = "Toyota",
            Model = "Corolla Hybrid",
            Trim = "LE",
            Price = 21990m,
            Mileage = 12000,
        };
        await service.UpsertAsync(candidate, run, CancellationToken.None);
        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);

        await service.UpsertAsync(candidate with { PushButtonStart = EquipmentStatus.Present }, run, CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.WindowSticker), reread.Vehicles.Single().StoredEquipment.PushButtonStart);
    }

    [Fact]
    public async Task Rank_ManualAbsentPushButtonStart_ExcludesTheCarWithTheManualStickerAsTheReason()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        VehicleEntity vehicle = await AddCorollaAsync(db);
        Assert.Contains("push-button start", ScoreOf(vehicle).UnconfirmedFeatures);

        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);
        Score score = ScoreOf(db.Vehicles.Single());

        Assert.False(score.Passes);
        Assert.Contains($"push-button start is absent (sticker (manual): {PdfSource}), which the scenario requires", score.FailureReasons);
        Assert.DoesNotContain("push-button start", score.UnconfirmedFeatures);
    }

    [Fact]
    public async Task Rank_ManualPresentOverATableThatSaysAbsent_TheManualReadingWins()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await AddCorollaAsync(db);
        FactoryTrimTable absentTable = FactoryTrimTable.Parse("""
            { "entries": [
              { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2025, "trim": "LE",
                "smartKeyEntry": "absent", "pushButtonStart": "absent", "source": "test sheet" }
            ] }
            """);

        await EquipmentCommand.SetAsync(db, Vin, EquipmentStatus.Present, EquipmentStatus.Present, null, PdfSource, CancellationToken.None);
        Score score = ScoreOf(db.Vehicles.Single(), absentTable);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Empty(score.UnconfirmedFeatures);
    }

    [Fact]
    public async Task Rank_ManualAbsentOverATableThatSaysPresent_StillExcludesTheCar()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await AddCorollaAsync(db);

        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);
        Score score = ScoreOf(db.Vehicles.Single(), PresentTable);

        Assert.False(score.Passes);
        Assert.Contains(score.FailureReasons, reason => reason.StartsWith("push-button start is absent (sticker (manual)"));
    }

    [Fact]
    public async Task EquipmentLines_ManualReading_PrintsTheSourceAndTheText()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await AddCorollaAsync(db);
        await EquipmentCommand.SetAsync(db, Vin, null, EquipmentStatus.Absent, null, PdfSource, CancellationToken.None);

        IReadOnlyList<string> lines = ShowRenderer.EquipmentLines(db.Vehicles.Single().StoredEquipment, DaughterScenario.Filters.RequiredFeatures);

        Assert.Contains("  push-button start: absent (sticker (manual): Toyota sticker PDF 2026-10-03)", lines);
    }

    [Theory]
    [InlineData("present", EquipmentStatus.Present)]
    [InlineData("Absent", EquipmentStatus.Absent)]
    [InlineData(" absent ", EquipmentStatus.Absent)]
    public void ParseStatus_PresentOrAbsent_ParsesIgnoringCase(string text, EquipmentStatus expected)
    {
        Assert.Equal(expected, EquipmentCommand.ParseStatus(text));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("yes")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseStatus_AnythingElse_IsNull(string? text)
    {
        Assert.Null(EquipmentCommand.ParseStatus(text));
    }

    [Fact]
    public async Task RunAsync_NoStatusGiven_FailsBeforeTouchingTheLedger()
    {
        int exitCode = await EquipmentCommand.RunAsync(Vin, null, null, null, PdfSource, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_NoSource_FailsBeforeTouchingTheLedger()
    {
        int exitCode = await EquipmentCommand.RunAsync(Vin, null, "absent", null, "  ", CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_StatusThatIsNeitherPresentNorAbsent_FailsBeforeTouchingTheLedger()
    {
        int exitCode = await EquipmentCommand.RunAsync(Vin, null, "unknown", null, PdfSource, CancellationToken.None);

        Assert.Equal(1, exitCode);
    }
}
