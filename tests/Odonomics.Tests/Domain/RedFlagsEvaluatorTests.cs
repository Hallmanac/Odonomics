using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class RedFlagsEvaluatorTests
{
    [Fact]
    public void Evaluate_NoIssues_ReturnsEmpty()
    {
        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(
            openRecallCount: 0,
            safetyOverallRating: 5,
            priorListings: [],
            currentPrice: null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_OpenRecalls_FlagsRecallCount()
    {
        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(
            openRecallCount: 2,
            safetyOverallRating: null,
            priorListings: [],
            currentPrice: null);

        Assert.Contains(flags, f => f.Contains("2 open NHTSA recall"));
    }

    [Fact]
    public void Evaluate_SafetyRatingBelowFourStars_FlagsIt()
    {
        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(
            openRecallCount: 0,
            safetyOverallRating: 3,
            priorListings: [],
            currentPrice: null);

        Assert.Contains(flags, f => f.Contains("3 star") && f.Contains("below 4"));
    }

    [Fact]
    public void Evaluate_SafetyRatingFourOrAbove_NoFlag()
    {
        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(
            openRecallCount: 0,
            safetyOverallRating: 4,
            priorListings: [],
            currentPrice: null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDecreasesBetweenListings_FlagsIt()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 50000),
            new("Dealer B", day2, 20500m, 48000),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, null);

        Assert.Contains(flags, f => f.Contains("mileage dropped from 50,000 to 48,000"));
    }

    [Fact]
    public void Evaluate_MileageNeverDecreases_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 48000),
            new("Dealer B", day2, 20500m, 50000),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_ThreeDealersWithinNinetyDays_FlagsDealerHopping()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 48000),
            new("Dealer B", day2, 20000m, 48100),
            new("Dealer C", day3, 20000m, 48200),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, null);

        Assert.Contains(flags, f => f.Contains("3 different dealers") && f.Contains("Dealer A") && f.Contains("Dealer C"));
    }

    [Fact]
    public void Evaluate_LongHistoryWithARecentDealerHopBurst_StillFlagsTheBurst()
    {
        // A VIN with an ordinary multi-year history (one dealer, or a slow trickle) plus a recent
        // burst of three dealers in nine days: the whole history spans years, but the burst itself
        // is exactly the pattern this rule exists to catch, so it must not get diluted away by the
        // years-old listings sitting earlier in the same array. Reproduces a real Marketcheck VIN
        // history observed during manual verification of this feature.
        DateTimeOffset yearsAgo1 = new(2020, 6, 25, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset yearsAgo2 = new(2020, 6, 26, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset burst1 = new(2026, 9, 13, 1, 58, 0, TimeSpan.Zero);
        DateTimeOffset burst2 = new(2026, 9, 13, 2, 36, 0, TimeSpan.Zero);
        DateTimeOffset burst3 = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Old Dealer A", yearsAgo1, 25765m, 60000),
            new("Old Dealer B", yearsAgo2, 25765m, 60000),
            new("Driver's Mart Usa", burst1, 19394m, 69599),
            new("Driver's Mart Sanford", burst2, 19394m, 69599),
            new("Holler Classic", burst3, 17995m, 69599),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, null);

        Assert.Contains(flags, f => f.Contains("3 different dealers") && f.Contains("Holler Classic"));
    }

    [Fact]
    public void Evaluate_ThreeDealersSpreadOverAYear_NoDealerHopFlag()
    {
        DateTimeOffset day1 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2024, 8, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 40000),
            new("Dealer B", day2, 21000m, 45000),
            new("Dealer C", day3, 22000m, 50000),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, null);

        Assert.DoesNotContain(flags, f => f.Contains("different dealers"));
    }

    [Fact]
    public void Evaluate_PriceWellAboveTrajectory_FlagsIt()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 40000),
            new("Dealer B", day2, 20500m, 41000),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, currentPrice: 25000m);

        Assert.Contains(flags, f => f.Contains("well above the $20,250 average"));
    }

    [Fact]
    public void Evaluate_PriceCloseToTrajectory_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 40000),
            new("Dealer B", day2, 20500m, 41000),
        ];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, currentPrice: 21000m);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_OnlyOnePriorListing_TrajectoryRuleSkipped()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings = [new("Dealer A", day1, 20000m, 40000)];

        IReadOnlyList<string> flags = RedFlagsEvaluator.Evaluate(0, null, listings, currentPrice: 40000m);

        Assert.Empty(flags);
    }
}
