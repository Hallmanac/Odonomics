using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

/// <summary>The path from a ledger vehicle's stored equipment through the trim table and the scorer, with
/// the shipped scenario's requirement of smart-key entry and push-button start.</summary>
public class EquipmentRankingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static readonly Scenario DaughterScenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static readonly FactoryTrimTable CorollaTable = FactoryTrimTable.Parse("""
        { "entries": [
          { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2025, "trim": "LE",
            "smartKeyEntry": "absent", "pushButtonStart": "absent", "source": "test sheet" }
        ] }
        """);

    /// <summary>The #3 JTDBCMFE7P3014805 shape: a 2023 Corolla Hybrid LE whose dealer text claimed push-button
    /// start, but whose window sticker listed only Keyless Entry, so the walk stored absent from the sticker.</summary>
    private static VehicleEntity CorollaHybrid(EquipmentStatus smartKey = EquipmentStatus.Unknown, EquipmentStatus pushButton = EquipmentStatus.Unknown) => new()
    {
        Vin = "JTDBCMFE7P3014805",
        Year = 2023,
        Make = "Toyota",
        Model = "Corolla Hybrid",
        Trim = "LE",
        Mileage = 41200,
        FirstSeen = RunTime,
        LastSeen = RunTime,
        SmartKeyEntry = smartKey,
        SmartKeyEntrySource = smartKey == EquipmentStatus.Unknown ? EquipmentSource.None : EquipmentSource.WindowSticker,
        PushButtonStart = pushButton,
        PushButtonStartSource = pushButton == EquipmentStatus.Unknown ? EquipmentSource.None : EquipmentSource.WindowSticker,
        Postings =
        [
            new PostingEntity
            {
                VehicleVin = "JTDBCMFE7P3014805",
                Source = "carvana",
                Url = "https://example.com/jtdbcmfe7p3014805",
                FirstSeen = RunTime,
                LastSeen = RunTime,
                PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 19990m, ObservedAt = RunTime }],
            },
        ],
    };

    [Fact]
    public void Score_StickerListsOnlyKeylessEntry_ExcludesTheCarForAbsentPushButtonStart()
    {
        VehicleEntity vehicle = CorollaHybrid(EquipmentStatus.Absent, EquipmentStatus.Absent);

        Score score = Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario);

        Assert.False(score.Passes);
        Assert.Contains("push-button start is absent (window sticker), which the scenario requires", score.FailureReasons);
        Assert.Contains("smart-key entry is absent (window sticker), which the scenario requires", score.FailureReasons);
    }

    [Fact]
    public void Score_NothingStoredAndAnEmptyTable_RanksTheCarWithBothFeaturesToConfirm()
    {
        VehicleEntity vehicle = CorollaHybrid();

        Score score = Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles, FactoryTrimTable.Empty), DaughterScenario);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Equal(["smart-key entry", "push-button start"], score.UnconfirmedFeatures);
    }

    [Fact]
    public void Score_TableSaysAbsentForAnUnknownStatus_ExcludesTheCarWithTheTableAsTheSource()
    {
        VehicleEntity vehicle = CorollaHybrid();

        Score score = Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles, CorollaTable), DaughterScenario);

        Assert.False(score.Passes);
        Assert.Contains("push-button start is absent (factory trim table), which the scenario requires", score.FailureReasons);
    }

    [Fact]
    public void Score_StickerPresentButTableSaysAbsent_TheStickerWins()
    {
        VehicleEntity vehicle = CorollaHybrid(EquipmentStatus.Present, EquipmentStatus.Present);

        Score score = Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles, CorollaTable), DaughterScenario);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Empty(score.UnconfirmedFeatures);
    }

    [Fact]
    public void Score_OnlyPushButtonStartIsKnown_NotesJustTheOtherFeatureToConfirm()
    {
        VehicleEntity vehicle = CorollaHybrid(pushButton: EquipmentStatus.Present);

        Score score = Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Equal(["smart-key entry"], score.UnconfirmedFeatures);
    }

    [Fact]
    public void EquipmentLines_ShowsBothStatusesWithTheirSourcesAndTheRequirementOutcome()
    {
        var equipment = new VehicleEquipment(
            new EquipmentFact(EquipmentStatus.Present, EquipmentSource.WindowSticker),
            new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.TrimTable));

        IReadOnlyList<string> lines = ShowRenderer.EquipmentLines(equipment, DaughterScenario.Filters.RequiredFeatures);

        Assert.Contains("  smart-key entry: present (window sticker)", lines);
        Assert.Contains("  push-button start: absent (factory trim table)", lines);
        Assert.Contains(lines, line => line.Contains("the scenario requires push-button start, so odo rank excludes this car"));
    }

    [Fact]
    public void EquipmentLines_UnknownRequiredFeature_PrintsItsStatusAndAConfirmNote()
    {
        IReadOnlyList<string> lines = ShowRenderer.EquipmentLines(VehicleEquipment.Unknown, DaughterScenario.Filters.RequiredFeatures);

        Assert.Contains("  smart-key entry: unknown", lines);
        Assert.Contains("  push-button start: unknown", lines);
        Assert.Contains(lines, line => line.Contains("confirm push-button start"));
    }

    [Fact]
    public void EquipmentLines_NothingRequired_PrintsTheStatusesAlone()
    {
        IReadOnlyList<string> lines = ShowRenderer.EquipmentLines(VehicleEquipment.Unknown, []);

        Assert.Equal(2, lines.Count);
    }
}
