using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

/// <summary>The path from a ledger vehicle's stored vehicle-history summary through the scorer: frame damage
/// excludes a car, a reported accident only warns, and a stated zero earns the "no accidents" marker.</summary>
public class HistoryRankingTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static readonly Scenario DaughterScenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static PostingEntity Posting(PostingHistory history, string source = "cargurus", int daysAgo = 0) => new()
    {
        VehicleVin = "4T1DAACK8SU095738",
        Source = source,
        Url = $"https://example.com/{source}/{daysAgo}",
        FirstSeen = RunTime.AddDays(-daysAgo),
        LastSeen = RunTime.AddDays(-daysAgo),
        History = history,
        PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 23249m, ObservedAt = RunTime.AddDays(-daysAgo) }],
    };

    private static VehicleEntity Camry(params PostingEntity[] postings) => new()
    {
        Vin = "4T1DAACK8SU095738",
        Year = 2025,
        Make = "Toyota",
        Model = "Camry Hybrid",
        Trim = "LE",
        Mileage = 49336,
        FirstSeen = RunTime,
        LastSeen = RunTime,
        Postings = [.. postings],
    };

    private static Score ScoreOf(VehicleEntity vehicle) =>
        Scorer.Score(RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario);

    [Fact]
    public void Score_FrameDamageStatedBesideThePrice_IsExcludedNamingTheSourceAndStillRaisesTheFlag()
    {
        // The #152 shape: a card or page printing "(Frame damage reported)" beside the price, with nothing else stated.
        VehicleEntity vehicle = Camry(Posting(new PostingHistory(FrameDamageStatement: "Frame damage reported")));

        Score score = ScoreOf(vehicle);

        Assert.False(score.Passes);
        Assert.Equal(["frame damage reported (CarGurus AutoCheck summary)"], score.FailureReasons);
        Assert.Contains("frame-damage", HistoryFlags.For(vehicle).Select(f => f.ShortTag));
    }

    [Fact]
    public void Score_FrameDamageOnlyOnAGonePosting_IsStillExcluded()
    {
        VehicleEntity vehicle = Camry(
            Posting(new PostingHistory(FrameDamageStatement: "Frame damage reported"), daysAgo: 10),
            Posting(PostingHistory.None, "carvana"));

        Assert.Contains("frame damage reported (CarGurus AutoCheck summary)", ScoreOf(vehicle).FailureReasons);
    }

    [Fact]
    public void Score_OneAccidentReported_RanksWithItsFlagAndNoMarker()
    {
        VehicleEntity vehicle = Camry(Posting(new PostingHistory("Clean title", 1, 1)));

        Score score = ScoreOf(vehicle);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.False(score.Vehicle.NoAccidentsStated);
        Assert.Equal(["accident-reported"], HistoryFlags.For(vehicle).Select(f => f.ShortTag));
    }

    [Fact]
    public void Score_RentalUseStated_RanksWithItsFlag()
    {
        VehicleEntity vehicle = Camry(Posting(new PostingHistory(UseStatement: "Reported as previous rental vehicle")));

        Assert.True(ScoreOf(vehicle).Passes);
        Assert.Equal(["rental-history"], HistoryFlags.For(vehicle).Select(f => f.ShortTag));
    }

    [Fact]
    public void Score_ZeroAccidentsStated_RanksWithTheMarkerAndNoFlag()
    {
        VehicleEntity vehicle = Camry(Posting(new PostingHistory("Clean title", 0, 1)));

        Score score = ScoreOf(vehicle);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.True(score.Vehicle.NoAccidentsStated);
        Assert.Empty(HistoryFlags.For(vehicle));
        Assert.Equal(["  [green]no accidents[/]"], ShowRenderer.HistoryMarkerLines(vehicle));
    }

    [Fact]
    public void Score_NoAccidentCountStated_PrintsNoMarkerAndStaysRanked()
    {
        VehicleEntity vehicle = Camry(Posting(new PostingHistory("Clean title", null, 1)), Posting(PostingHistory.None, "carvana"));

        Score score = ScoreOf(vehicle);

        Assert.True(score.Passes, string.Join("; ", score.FailureReasons));
        Assert.False(score.Vehicle.NoAccidentsStated);
        Assert.Empty(ShowRenderer.HistoryMarkerLines(vehicle));
    }

    [Fact]
    public void Score_ZeroFromOneSourceAndOneAccidentFromAnother_PrintsNoMarker()
    {
        VehicleEntity vehicle = Camry(
            Posting(new PostingHistory(AccidentCount: 0)),
            Posting(new PostingHistory(AccidentCount: 1), "autotrader"));

        Assert.False(ScoreOf(vehicle).Vehicle.NoAccidentsStated);
    }

    [Fact]
    public void Score_AccidentCountsFromAnOlderPostingOfTheSameSource_AreReplacedByTheNewestOne()
    {
        VehicleEntity vehicle = Camry(
            Posting(new PostingHistory(AccidentCount: 1), daysAgo: 10),
            Posting(new PostingHistory(AccidentCount: 0), daysAgo: 1));

        Assert.True(ScoreOf(vehicle).Vehicle.NoAccidentsStated);
    }
}
