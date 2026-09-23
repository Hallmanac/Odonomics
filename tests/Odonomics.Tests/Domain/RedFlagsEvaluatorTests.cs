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
            currentPrice: null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_RecallWithRemedyAvailable_NoFlag()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [new RecallForFlagging(RemedyAvailable: true)],
            safetyOverallRating: null,
            priorListings: [],
            currentPrice: null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_RecallWithNoRemedyAvailable_FlagsIt()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [new RecallForFlagging(RemedyAvailable: false), new RecallForFlagging(RemedyAvailable: true)],
            safetyOverallRating: null,
            priorListings: [],
            currentPrice: null).Flags;

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
            currentPrice: null).Flags;

        Assert.Contains(flags, f => f.Detail.Contains("3 star") && f.Detail.Contains("below 4"));
    }

    [Fact]
    public void Evaluate_SafetyRatingFourOrAbove_NoFlag()
    {
        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate(
            recalls: [],
            safetyOverallRating: 4,
            priorListings: [],
            currentPrice: null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDropAboveThresholdOnDifferentDays_FlagsIt()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 50000),
            new("Dealer B", day2, day2, 20500m, 48000),
        ];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(result.Flags);
        Assert.Equal("mileage-drop", flag.ShortTag);
        Assert.Contains("mileage dropped from 50,000 to 48,000", flag.Detail);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Evaluate_MileageNeverDecreases_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 48000),
            new("Dealer B", day2, day2, 20500m, 50000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

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
            new("Dealer A", day1, day1, 20000m, 85000),
            new("Dealer B", day2, day2, 20500m, 84599),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_MileageDropToZero_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 9021),
            new("Dealer B", day2, day2, 20500m, 0),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_SameDayMileageDrop_NoFlag()
    {
        DateTimeOffset sameDayMorning = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset sameDayEvening = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", sameDayMorning, sameDayMorning, 20000m, 40015),
            new("Dealer A", sameDayEvening, sameDayEvening, 20000m, 40000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_PlaceholderStraddledByARealRollback_StillFlagsIt()
    {
        // A placeholder reading between two real readings must not become the baseline for the next
        // comparison: 85,000 -> 0 (placeholder, excluded) -> 42,000 is a real 43,000-mile rollback
        // that the placeholder must not be allowed to mask. All three rows share one dealer name, but
        // the two real readings are two months apart, well past the same-seller "consecutive or
        // overlapping" gate, so this still flags rather than softening to a note.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 85000),
            new("Dealer A", day2, day2, 20000m, 0),
            new("Dealer A", day3, day3, 20000m, 42000),
        ];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(result.Flags);
        Assert.Equal("mileage-drop", flag.ShortTag);
        Assert.Contains("mileage dropped from 85,000 to 42,000", flag.Detail);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Evaluate_SameDayBadScrapeStraddledByARealRollback_StillFlagsIt()
    {
        // A same-day duplicate scrape must not become the baseline for the next comparison either:
        // 50,000 (Jan 1) -> 40,000 (Jan 1, excluded as a same-day duplicate) -> 41,000 (Jan 5) is a
        // real 9,000-mile rollback from the day's first reading that must not be masked.
        DateTimeOffset sameDayMorning = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset sameDayEvening = new(2026, 1, 1, 20, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", sameDayMorning, sameDayMorning, 20000m, 50000),
            new("Dealer A", sameDayEvening, sameDayEvening, 20000m, 40000),
            new("Dealer B", later, later, 20000m, 41000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("mileage-drop", flag.ShortTag);
        Assert.Contains("mileage dropped from 50,000 to 41,000", flag.Detail);
    }

    [Fact]
    public void Evaluate_SameSellerGroupConsecutiveDayMileageDrop_RecordsANoteNotAFlag()
    {
        // Reproduces the operator's 2026-09-23 odo show on 4T1DAACK9TU267793 (a 2026 Camry Hybrid XSE):
        // the placeholder-mileage rows in February are dropped as usual, then Daytona Toyota's own
        // listing corrects its odometer reading from 4,703 to 3,852 miles between Sep 9 and Sep 10,
        // a same-dealer, next-day continuation of the same listing rather than a rolled-back
        // odometer on a resold car, so it becomes a note instead of a red flag.
        DateTimeOffset feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset feb4 = new(2026, 2, 4, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset feb5 = new(2026, 2, 5, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset feb8 = new(2026, 2, 8, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset mar22 = new(2026, 3, 22, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sep9 = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sep10 = new(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sep18 = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sep19 = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset sep22 = new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Daytona Toyota", feb1, feb4, 44269m, 0),
            new("Daytona Toyota", feb5, mar22, 44020m, 0),
            new("The Smartlots", feb8, mar22, 44020m, 0),
            new("Daytona Toyota", sep9, sep9, 40799m, 4703),
            new("Daytona Toyota", sep10, sep18, 40799m, 3852),
            new("Daytona Toyota", sep19, sep22, 40599m, 3852),
        ];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.DoesNotContain(result.Flags, f => f.ShortTag == "mileage-drop");
        Assert.Contains("mileage corrected 4,703 to 3,852 at Daytona Toyota on Sep 10", Assert.Single(result.Notes));

        // The Smartlots' own placeholder-zero row overlaps Daytona Toyota's placeholder-zero row in
        // time, but 0 miles is never a real reading, so the two must not merge into one seller group
        // just because both happen to read 0. They only share a window with an unrelated dealer, not
        // a real mileage match.
        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(listings);
        Assert.Contains(groups, g => g.DealerNames.Contains("The Smartlots") && !g.DealerNames.Contains("Daytona Toyota"));
    }

    [Fact]
    public void Evaluate_SameSellerButGapBetweenWindows_StillFlagsIt()
    {
        // Same dealer both times (so the seller-group rule merges them by name stem), but the two
        // real readings are a month apart rather than consecutive or overlapping days, so this is
        // not the same "same listing, corrected reading" shape as the Daytona Toyota note case and
        // still flags by the existing thresholds.
        DateTimeOffset sep9 = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset oct9 = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Daytona Toyota", sep9, sep9, 40799m, 4703),
            new("Daytona Toyota", oct9, oct9, 40599m, 3852),
        ];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(result.Flags);
        Assert.Equal("mileage-drop", flag.ShortTag);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Evaluate_ThreeDealersWithinNinetyDays_FlagsDealerHopping()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 48000),
            new("Dealer B", day2, day2, 20000m, 48100),
            new("Dealer C", day3, day3, 20000m, 48200),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("3-sellers", flag.ShortTag);
        Assert.Contains("3 different sellers", flag.Detail);
        Assert.Contains("Dealer A", flag.Detail);
        Assert.Contains("Dealer C", flag.Detail);
    }

    [Fact]
    public void Evaluate_ThreeSequentialNonOverlappingWindowsWithRisingMileage_YieldsThreeSellersAndFlag()
    {
        // The seller-group rule's other half: three windows that never overlap or touch, each with
        // mileage that genuinely moved from the one before, are three real seller changes and must
        // still count as three distinct sellers (and flag), the same as the old raw-dealer-count rule
        // did, just derived from the window/mileage shape now rather than from three raw names.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Seller One", day1, day1, 20000m, 40000),
            new("Seller Two", day2, day2, 20500m, 41000),
            new("Seller Three", day3, day3, 21000m, 42000),
        ];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        RedFlag flag = Assert.Single(result.Flags);
        Assert.Equal("3-sellers", flag.ShortTag);

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(listings);
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void Evaluate_ListingsWithNoDealerName_NeverCountTowardSellerFlag()
    {
        // A listing with no dealer name at all carries no evidence about who the seller was, so it
        // must never count toward the seller-count flag, the same as the raw-dealer-name rule this
        // replaced ignored a nameless listing entirely (see MarketcheckHistoryClient, which reports
        // Dealer as null whenever seller_name is absent).
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new(null, day1, day1, 20000m, 40000),
            new(null, day2, day2, 20500m, 41000),
            new(null, day3, day3, 21000m, 42000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_UnknownMileageBetweenTwoNamedSellers_StillCountsAsAChange()
    {
        // A missing mileage reading is never proof the car sat with the same owner; only two real,
        // equal readings block counting a seller change. Three sequential, distinctly-named dealers,
        // the middle one with no reported mileage, must still count as three sellers, the same as
        // three raw distinct dealer names did before this rule existed.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 40000),
            new("Dealer B", day2, day2, 20500m, null),
            new("Dealer C", day3, day3, 21000m, 42000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("3-sellers", flag.ShortTag);
    }

    [Fact]
    public void Evaluate_AllPlaceholderMileageAcrossDistinctDealers_StillCountsSellers()
    {
        // All-placeholder mileage across genuinely different, non-overlapping dealers must not
        // silently collapse the seller count to one: PlaceholderMileageMax already distrusts a
        // near-zero reading as evidence of sameness in BuildSellerGroups, and CountSellers must
        // apply that same distrust rather than reading two equal placeholders as proof the mileage
        // stayed put.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 0),
            new("Dealer B", day2, day2, 20000m, 0),
            new("Dealer C", day3, day3, 20000m, 0),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("3-sellers", flag.ShortTag);
    }

    [Fact]
    public void Evaluate_SyndicatedOverlappingSameMileageListings_YieldsOneSellerGroupAndNoFlag()
    {
        // Reproduces the operator's 2026-09-23 odo show on 4T1DAACK7SU000408 (a 2025 Camry Hybrid):
        // 16 ALM-group and affiliated rooftops, all at 36,005 miles, all with overlapping listing
        // windows between Jan 24 and Mar 5 (synthesized here as one shared window per rooftop, since
        // the recorded run captured the rooftop list and the shared window bounds but not each row's
        // exact first/last dates). The old dealer-name-stem rule missed this because "Alm" and "ALM"
        // differ only in casing, and because Carrollton Hyundai, Genesis of Macon, and Five Star
        // Hyundai share no name with the group at all; the window-and-mileage rule catches it because
        // every rooftop's window overlaps every other rooftop's, at the identical mileage.
        DateTimeOffset windowStart = new(2026, 1, 24, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset windowEnd = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        string[] rooftops =
        [
            "Carrollton Hyundai", "Alm Hyundai Florence", "ALM Chevrolet South", "Alm Kia Perry",
            "ALM Mazda Macon", "Alm Hyundai Athens", "ALM Mazda South", "Alm Cdjr Macon",
            "ALM Ford Marietta", "Alm Chrysler Dodge Jeep Ram Perry", "Alm Kia South", "Alm Nissan Newnan",
            "Genesis of Macon", "Alm Hyundai West", "Five Star Hyundai of Macon", "Five Star Hyundai of Warner Robins",
        ];
        List<VinHistoryPoint> listings = [.. rooftops.Select(name => new VinHistoryPoint(name, windowStart, windowEnd, 27995m, 36005))];

        EvaluationResult result = RedFlagsEvaluator.Evaluate([], null, listings, null);

        Assert.DoesNotContain(result.Flags, f => f.ShortTag.EndsWith("-sellers"));

        SellerGroupSummary group = Assert.Single(RedFlagsEvaluator.GroupBySeller(listings));
        Assert.Equal(16, group.DealerNames.Count);
        Assert.Equal(36005, group.MinMileage);
        Assert.Equal(36005, group.MaxMileage);
    }

    [Fact]
    public void Evaluate_LongHistoryWithARecentDealerHopBurst_StillFlagsTheBurst()
    {
        // A VIN with an ordinary multi-year history (one dealer, or a slow trickle) plus a recent
        // burst of three dealers in a handful of days: the whole history spans years, but the burst
        // itself is exactly the pattern this rule exists to catch, so it must not get diluted away by
        // the years-old listings sitting earlier in the same array. Same shape as a real Marketcheck
        // VIN history observed during manual verification of this feature, but with the burst
        // dealers' mileage bumped a little at each step (a realistic dealer-to-dealer transport
        // distance) instead of the recorded unchanged reading, so each transition is a genuine
        // seller change under the window/mileage rule rather than a same-mileage syndication
        // artifact; the literal recorded readings (all three burst dealers at one unchanged mileage)
        // are covered separately below, since they read as syndication under this rule and no
        // longer flag.
        DateTimeOffset yearsAgo1 = new(2020, 6, 25, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset yearsAgo2 = new(2020, 6, 26, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset burst1 = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset burst2 = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset burst3 = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Old Dealer A", yearsAgo1, yearsAgo1, 25765m, 60000),
            new("Old Dealer B", yearsAgo2, yearsAgo2, 25765m, 60000),
            new("Driver's Mart Usa", burst1, burst1, 19394m, 69599),
            new("Driver's Mart Sanford", burst2, burst2, 19394m, 69640),
            new("Holler Classic", burst3, burst3, 17995m, 69680),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.Contains(flags, f => f.Detail.Contains("different sellers") && f.Detail.Contains("Holler Classic"));
    }

    [Fact]
    public void Evaluate_RecordedDealerHopBurstAtUnchangedMileage_NoLongerFlags()
    {
        // The literal recorded reading from manual verification of this feature: three dealers
        // within days of each other, all at one unchanged 69,599 miles (Driver's Mart Usa Sep 13
        // 01:58, Driver's Mart Sanford Sep 13 02:36, Holler Classic Sep 17). Under the window/mileage
        // rule, Driver's Mart Usa and Sanford merge into one seller group (touching windows,
        // identical real mileage, and a shared name stem besides), and Holler Classic then resumes
        // at that same unchanged mileage, so it is folded in rather than counted as a third seller:
        // this reads as syndication, not a genuine dealer-to-dealer hop, so it must not flag, unlike
        // the (deliberately modified) mileage-rising version above.
        DateTimeOffset yearsAgo1 = new(2020, 6, 25, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset yearsAgo2 = new(2020, 6, 26, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset burst1 = new(2026, 9, 13, 1, 58, 0, TimeSpan.Zero);
        DateTimeOffset burst2 = new(2026, 9, 13, 2, 36, 0, TimeSpan.Zero);
        DateTimeOffset burst3 = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Old Dealer A", yearsAgo1, yearsAgo1, 25765m, 60000),
            new("Old Dealer B", yearsAgo2, yearsAgo2, 25765m, 60000),
            new("Driver's Mart Usa", burst1, burst1, 19394m, 69599),
            new("Driver's Mart Sanford", burst2, burst2, 19394m, 69599),
            new("Holler Classic", burst3, burst3, 17995m, 69599),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_ThreeDealersSpreadOverAYear_NoDealerHopFlag()
    {
        DateTimeOffset day1 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2024, 8, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 40000),
            new("Dealer B", day2, day2, 21000m, 45000),
            new("Dealer C", day3, day3, 22000m, 50000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

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
            new("Schaller Honda", day1, day1, 20000m, 40000),
            new("SCHALLER HONDA", day2, day2, 20000m, 40100),
            new("Schaller Honda.", day3, day3, 20000m, 40200),
            new("Schaller Honda Subaru Mitsubishi", day4, day4, 20000m, 40300),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_ManyGenuinelyDistinctSellers_FlagsAndCapsThePrintedListAtThreeNames()
    {
        // Reproduces the operator's 2026-09-22 run: a syndication feed relisted the car under 34
        // genuinely different rooftop names (AutoNation, Gary Yeomans, and Mercedes-Benz Of
        // locations among them), which used to dump all 34 names into the flag text. The count
        // still reflects reality, but the printed names cap at three plus "and N more". Mileage
        // rises by one mile a day, so no pair shares identical mileage and every window/mileage
        // check falls through to the dealer-name check, which finds no shared stem among 34
        // genuinely distinct names.
        DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings = [.. Enumerable.Range(0, 34)
            .Select(i => new VinHistoryPoint($"Rooftop {i}", start.AddDays(i), start.AddDays(i), 20000m, 40000 + i))];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        RedFlag flag = Assert.Single(flags);
        Assert.Equal("34-sellers", flag.ShortTag);
        Assert.Contains("Rooftop 0, Rooftop 1, Rooftop 2 and 31 more", flag.Detail);
        Assert.DoesNotContain("Rooftop 3,", flag.Detail);
    }

    [Fact]
    public void Evaluate_LateArrivingBridgeNameMergesAlreadySplitGroups_NoFalseSellerCount()
    {
        // "Schaller Honda Subaru" and "Schaller Honda Mitsubishi" diverge at their third word, so
        // they start as two separate groups; "Schaller Honda" arrives after both and is a
        // word-prefix of each, so it must merge both groups into one rather than only the first
        // group it happens to match. A prior version compared a candidate against only a group's
        // current representative, so this bare name merged into just one of the two groups,
        // leaving two returned sellers where one was a word-prefix of the other (an outright
        // invariant violation), and a fourth, genuinely distinct dealer in the same window pushed
        // the count to 3 and tripped the default threshold as a false positive.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day4 = new(2026, 1, 25, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Schaller Honda Subaru", day1, day1, 20000m, 40000),
            new("Schaller Honda Mitsubishi", day2, day2, 20000m, 40100),
            new("Schaller Honda", day3, day3, 20000m, 40200),
            new("CarMax Orlando", day4, day4, 20000m, 40300),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null).Flags;

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_DealerCountThresholdRaised_NoLongerFiresAtThree()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day3 = new(2026, 2, 10, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 48000),
            new("Dealer B", day2, day2, 20000m, 48100),
            new("Dealer C", day3, day3, 20000m, 48200),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, null, dealerCountThreshold: 4).Flags;

        Assert.DoesNotContain(flags, f => f.ShortTag.EndsWith("-sellers"));
    }

    [Fact]
    public void Evaluate_PriceWellAboveTrajectory_FlagsIt()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 40000),
            new("Dealer B", day2, day2, 20500m, 41000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 25000m).Flags;

        Assert.Contains(flags, f => f.Detail.Contains("well above the $20,250 average"));
    }

    [Fact]
    public void Evaluate_PriceCloseToTrajectory_NoFlag()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings =
        [
            new("Dealer A", day1, day1, 20000m, 40000),
            new("Dealer B", day2, day2, 20500m, 41000),
        ];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 21000m).Flags;

        Assert.Empty(flags);
    }

    [Fact]
    public void Evaluate_OnlyOnePriorListing_TrajectoryRuleSkipped()
    {
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> listings = [new("Dealer A", day1, day1, 20000m, 40000)];

        IReadOnlyList<RedFlag> flags = RedFlagsEvaluator.Evaluate([], null, listings, currentPrice: 40000m).Flags;

        Assert.Empty(flags);
    }
}
