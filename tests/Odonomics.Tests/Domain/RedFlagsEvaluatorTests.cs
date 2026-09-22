using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class RedFlagsEvaluatorTests
{
    [Fact]
    public void Evaluate_NoIssues_ReturnsEmpty()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [],
            safetyOverallRating: 5,
            priorListings: [],
            currentPrice: null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_RecallWithRemedyAvailable_NoFlag()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [new RecallForFlagging(RemedyAvailable: true)],
            safetyOverallRating: null,
            priorListings: [],
            currentPrice: null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_RecallWithNoRemedyAvailable_FlagsIt()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [new RecallForFlagging(RemedyAvailable: false), new RecallForFlagging(RemedyAvailable: true)],
            safetyOverallRating: null,
            priorListings: [],
            currentPrice: null);

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("no-remedy-recall", flag.ShortTag);
        Assert.Contains("1 open recall", flag.Detail);
        Assert.Contains("no remedy available", flag.Detail);
    }

    [Fact]
    public void Evaluate_SafetyRatingBelowFourStars_FlagsIt()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [],
            safetyOverallRating: 3,
            priorListings: [],
            currentPrice: null);

        Assert.Contains(flags, f => f.Detail.Contains("3 star") && f.Detail.Contains("below 4"));
    }

    [Fact]
    public void Evaluate_SafetyRatingFourOrAbove_NoFlag()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [],
            safetyOverallRating: 4,
            priorListings: [],
            currentPrice: null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDropAboveThresholdOnDifferentDays_FlagsIt()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 50000),
            new("Dealer B", day2, 20500m, 48000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("mileage-drop", flag.ShortTag);
        Assert.Contains("mileage dropped from 50,000 to 48,000", flag.Detail);
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDropUnderThreshold_NoFlag()
    {
        // 85,000 to 84,599 is a 401-mile drop: under both the 500-mile floor and 1% of 85,000
        // (850 miles), so the larger threshold (850) governs and this must not fire.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 85000),
            new("Dealer B", day2, 20500m, 84599),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDropToZero_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, 20000m, 9021),
            new("Dealer B", day2, 20500m, 0),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_SameDayMileageDrop_NoFlag()
    {
        DateTimeOffset sameDayMorning = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset sameDayEvening = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", sameDayMorning, 20000m, 40015),
            new("Dealer A", sameDayEvening, 20000m, 40000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("3-sellers", flag.ShortTag);
        Assert.Contains("3 different sellers", flag.Detail);
        Assert.Contains("Dealer A", flag.Detail);
        Assert.Contains("Dealer C", flag.Detail);
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.Contains(flags, f => f.Detail.Contains("different sellers") && f.Detail.Contains("Holler Classic"));
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.DoesNotContain(flags, f => f.Detail.Contains("different sellers"));
    }

    [Fact]
    public void Evaluate_SchallerNameVariantsWithinWindow_MergeIntoOneSellerAndDoNotFlag()
    {
        // Reproduces the operator's 2026-09-22 run: the same physical Schaller Honda rooftop was
        // scraped under four spellings (casing, punctuation, and a franchise-name suffix), which
        // used to look like four different dealers hopping the car around.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day4 = new(2026, 1, 30, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Schaller Honda", day1, 20000m, 40000),
            new("SCHALLER HONDA", day2, 20000m, 40100),
            new("Schaller Honda.", day3, 20000m, 40200),
            new("Schaller Honda Subaru Mitsubishi", day4, 20000m, 40300),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_ManyGenuinelyDistinctSellers_FlagsAndCapsThePrintedListAtThreeNames()
    {
        // Reproduces the operator's 2026-09-22 run: a syndication feed relisted the car under 34
        // genuinely different rooftop names (AutoNation, Gary Yeomans, and Mercedes-Benz Of
        // locations among them), which used to dump all 34 names into the flag text. The count
        // still reflects reality, but the printed names cap at three plus "and N more".
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings = [.. Enumerable.Range(0, 34)
            .Select(i => new VinHistoryPoint($"Rooftop {i}", start.AddDays(i), 20000m, 40000 + i))];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("34-sellers", flag.ShortTag);
        Assert.Contains("Rooftop 0, Rooftop 1, Rooftop 2 and 31 more", flag.Detail);
        Assert.DoesNotContain("Rooftop 3,", flag.Detail);
    }

    [Fact]
    public void Evaluate_DealerCountThresholdRaised_NoLongerFiresAtThree()
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null, dealerCountThreshold: 4);

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 25000m);

        Assert.Contains(flags, f => f.Detail.Contains("well above the $20,250 average"));
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

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 21000m);

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_OnlyOnePriorListing_TrajectoryRuleSkipped()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings = [new("Dealer A", day1, 20000m, 40000)];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 40000m);

        Assert.Empty(flags);
    }
}
