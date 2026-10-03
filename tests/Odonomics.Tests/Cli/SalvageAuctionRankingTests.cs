using Odonomics.Auctions;
using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Tests.Auctions;

namespace Odonomics.Tests.Cli;

/// <summary>The path from a vehicle's stored `odo title check` result through the scorer: a found Copart or IAA
/// sale excludes the vehicle from `odo rank`, naming the sale, and no other result does.</summary>
public class SalvageAuctionRankingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static readonly Scenario DaughterScenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    /// <summary>The 2025 Corolla Hybrid the first salvage-auction hit was found on, priced under the ceiling, with
    /// the stored result of looking it up in the archives (none when <paramref name="result"/> is null).</summary>
    private static VehicleEntity CorollaHybrid(AuctionLookupResult? result)
    {
        var vehicle = new VehicleEntity
        {
            Vin = AuctionFixtures.CorollaHybridVin,
            Year = 2025,
            Make = "Toyota",
            Model = "Corolla Hybrid",
            Trim = "XLE",
            Mileage = 41200,
            FirstSeen = RunTime,
            LastSeen = RunTime,
            Postings =
            [
                new PostingEntity
                {
                    VehicleVin = AuctionFixtures.CorollaHybridVin,
                    Source = "carvana",
                    Url = "https://example.com/jtdbcmfexs3070309",
                    FirstSeen = RunTime,
                    LastSeen = RunTime,
                    PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 19990m, ObservedAt = RunTime }],
                },
            ],
        };
        if (result is not null)
        {
            AuctionChecks.Apply(vehicle, result, RunTime);
        }

        return vehicle;
    }

    private static AuctionLookupResult StoredFromTheRecordedLotPage()
    {
        AuctionPageReading reading = AuctionPageParser.Parse(
            AuctionFixtures.CorollaHybridVin,
            AuctionFixtures.CorollaHybridLotUrl,
            "2025 Toyota Corolla JTDBCMFEXS3070309 - BidCars",
            AuctionFixtures.Read("bid-cars-lot-JTDBCMFEXS3070309.txt"));
        return new AuctionLookupResult(AuctionCheckOutcome.Found, reading.Record, null);
    }

    private static Score Score(VehicleEntity vehicle) =>
        Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario);

    [Fact]
    public void Score_StoredCopartSaleForTheCorollaHybrid_ExcludesItNamingTheSale()
    {
        Score score = Score(CorollaHybrid(StoredFromTheRecordedLotPage()));

        Assert.False(score.Passes);
        Assert.Equal(["sold at salvage auction: Copart 2026-07-16, Salvage certificate (CA)"], score.FailureReasons);
        Assert.Empty(RankRenderer.RankedVehicles([score]));
    }

    [Fact]
    public void Score_FoundSaleOnACleanTitleDocument_StillExcludes()
    {
        var record = new AuctionRecord("IAA", "41234567", new DateOnly(2026, 8, 2), "Clean title", null, null, null, null, null, null);

        Score score = Score(CorollaHybrid(new AuctionLookupResult(AuctionCheckOutcome.Found, record, null)));

        Assert.Equal(["sold at salvage auction: IAA 2026-08-02, Clean title"], score.FailureReasons);
    }

    [Fact]
    public void Score_FoundSaleThatPrintedOnlyALotNumber_ExcludesWithoutInventingDetails()
    {
        var record = new AuctionRecord(null, "41234567", null, null, "Front end", null, null, null, null, null);

        Score score = Score(CorollaHybrid(new AuctionLookupResult(AuctionCheckOutcome.Found, record, null)));

        Assert.Equal(["sold at salvage auction: no sale details printed"], score.FailureReasons);
    }

    [Fact]
    public void Score_NotFoundResult_StaysRanked()
    {
        Score score = Score(CorollaHybrid(new AuctionLookupResult(AuctionCheckOutcome.NotFound, null, null)));

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.Single(RankRenderer.RankedVehicles([score]));
    }

    [Fact]
    public void Score_CouldNotReadResult_StaysRanked()
    {
        Score score = Score(CorollaHybrid(new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, "captcha")));

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
    }

    [Fact]
    public void Score_NeverChecked_StaysRanked()
    {
        Score score = Score(CorollaHybrid(null));

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
    }

    [Fact]
    public void SelectVehiclesToResearch_StoredSale_SkipsTheVehicleTheRankExcludes()
    {
        VehicleEntity sold = CorollaHybrid(StoredFromTheRecordedLotPage());

        Assert.Empty(ResearchCommand.SelectVehiclesToResearch([sold], DaughterScenario, NoCoverage));
        Assert.Single(ResearchCommand.SelectVehiclesToResearch([CorollaHybrid(null)], DaughterScenario, NoCoverage));
    }
}
