using System.Text.Json;
using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Marketcheck;

namespace Odonomics.Tests.Cli;

/// <summary>The path from a vehicle's stored Marketcheck VIN history through the scorer: a history row from a
/// salvage or repairable-vehicle seller excludes the vehicle from `odo rank`, naming the seller and the date, and a
/// history of ordinary dealers does not. The `salvage-seller` red flag itself is covered by
/// <see cref="Domain.SalvageSellerFlagTests"/>.</summary>
public class SalvageSellerRankingTests
{
    private const string Vin = "4T1DAACK1SU115103";
    private static readonly DateTimeOffset RunTime = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static readonly Scenario DaughterScenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static DateTimeOffset Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static VinHistoryListing Row(string dealer, DateTimeOffset firstSeen, decimal? price, int? mileage) =>
        new(dealer, null, null, firstSeen, firstSeen, price, mileage, null);

    /// <summary>The "all listings" rows of notes/show-4T1DAACK1SU115103.txt in the project home: a 2025 Camry
    /// Hybrid LE that sold through Salvage Autos Auction at 7,500 dollars and 9,068 miles on 2025-08-26, then
    /// through Ridesafely, Bid N Drive Inc and Auto4export Llc the next day, and is listed at HGreg Orlando now.</summary>
    private static readonly VinHistoryListing[] CamryHybridHistory =
    [
        Row("Salvage Autos Auction", Day(2025, 8, 26), 7500m, 9068),
        Row("Ridesafely", Day(2025, 8, 27), null, 9068),
        Row("Bid N Drive Inc", Day(2025, 8, 27), null, 9068),
        Row("Auto4export Llc", Day(2025, 8, 27), null, 9068),
        Row("HGreg Orlando", Day(2026, 9, 20), 24997m, 13561),
    ];

    private static VehicleEntity CamryHybrid(IReadOnlyList<VinHistoryListing>? history)
    {
        var vehicle = new VehicleEntity
        {
            Vin = Vin,
            Year = 2025,
            Make = "Toyota",
            Model = "Camry Hybrid",
            Trim = "LE",
            Mileage = 13561,
            FirstSeen = RunTime,
            LastSeen = RunTime,
            Postings =
            [
                new PostingEntity
                {
                    VehicleVin = Vin,
                    Source = "cars.com",
                    Url = "https://example.com/4t1daack1su115103",
                    FirstSeen = RunTime,
                    LastSeen = RunTime,
                    PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 24997m, ObservedAt = RunTime }],
                },
            ],
        };
        if (history is not null)
        {
            vehicle.VinRecord = new VinRecordEntity
            {
                Vin = Vin,
                DecodedAt = RunTime,
                DecodeRawJson = "",
                HistoryRawJson = JsonSerializer.Serialize(history),
            };
        }

        return vehicle;
    }

    private static Score Score(VehicleEntity vehicle) =>
        Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario);

    [Fact]
    public void Score_CamryHybridHistoryThroughSalvageAutosAuction_ExcludesItNamingTheSellerAndDate()
    {
        Score score = Score(CamryHybrid(CamryHybridHistory));

        Assert.False(score.Passes);
        Assert.Equal(["listed by a salvage seller: Salvage Autos Auction 2025-08-26"], score.FailureReasons);
        Assert.Empty(RankRenderer.RankedVehicles([score]));
    }

    [Fact]
    public void Score_HistoryWhereOnlyTheLaterRowsAreSalvageSellers_NamesTheEarliestOne()
    {
        VinHistoryListing[] history =
        [
            Row("Ordinary Motors", Day(2025, 3, 1), 30000m, 10),
            Row("Auto4export Llc", Day(2025, 8, 27), null, 9068),
            Row("Ridesafely", Day(2025, 8, 28), null, 9068),
        ];

        Score score = Score(CamryHybrid(history));

        Assert.Equal(["listed by a salvage seller: Auto4export Llc 2025-08-27"], score.FailureReasons);
    }

    [Fact]
    public void Score_SalvageSellerRowWithNoDate_ExcludesWithoutInventingOne()
    {
        VinHistoryListing[] history = [new("Copart", null, null, null, null, null, 40000, null)];

        Score score = Score(CamryHybrid(history));

        Assert.Equal(["listed by a salvage seller: Copart, date unknown"], score.FailureReasons);
    }

    [Fact]
    public void Score_OrdinaryDealersAndADealerNamedForManheim_StaysRanked()
    {
        VinHistoryListing[] history =
        [
            Row("Toyota of Orlando", Day(2025, 6, 1), 28000m, 12),
            Row("Manheim Orlando", Day(2025, 7, 15), null, 6400),
            Row("HGreg Orlando", Day(2026, 9, 20), 24997m, 13561),
        ];

        Score score = Score(CamryHybrid(history));

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Single(RankRenderer.RankedVehicles([score]));
    }

    [Fact]
    public void Score_NeverResearched_StaysRanked()
    {
        Score score = Score(CamryHybrid(null));

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
    }

    [Fact]
    public void Score_ResearchedWithNoHistoryFetched_StaysRanked()
    {
        VehicleEntity vehicle = CamryHybrid(null);
        vehicle.VinRecord = new VinRecordEntity { Vin = Vin, DecodedAt = RunTime, DecodeRawJson = "" };

        Score score = Score(vehicle);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
    }

    [Fact]
    public void RedFlagsForCached_CamryHybridHistory_KeepsTheSalvageSellerFlag()
    {
        VehicleEntity vehicle = CamryHybrid(CamryHybridHistory);

        RedFlag flag = Assert.Single(VinResearchService.RedFlagsForCached(vehicle.VinRecord!, 24997m), f => f.ShortTag == "salvage-seller");

        Assert.Contains("Salvage Autos Auction on 2025-08-26", flag.Detail);
    }

    [Fact]
    public void SelectVehiclesToResearch_SalvageSellerHistory_SkipsTheVehicleTheRankExcludes()
    {
        Assert.Empty(ResearchCommand.SelectVehiclesToResearch([CamryHybrid(CamryHybridHistory)], DaughterScenario, NoCoverage));
        Assert.Single(ResearchCommand.SelectVehiclesToResearch([CamryHybrid(null)], DaughterScenario, NoCoverage));
    }
}
