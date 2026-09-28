using System.Text.RegularExpressions;
using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Walk;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

/// <summary>Proves `odo rank` shows each listing site's own deal badge and dealer rating in one short
/// Site field and that the field is display only: the same fixture ledger ranked with and without its
/// badges comes out in the same order with the same cost figures.</summary>
[Collection(NoColorEnvironmentCollection.Name)]
public class RankSiteBadgeTests
{
    private static readonly DateTimeOffset RunTime = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyDictionary<string, DateTimeOffset> NoCoverage = new Dictionary<string, DateTimeOffset>();
    private static Scenario DaughterScenario { get; } = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static PostingEntity Posting(string source, string vin, decimal price, decimal? shippingFee = null, params (string Name, string Value)[] attributes) => new()
    {
        VehicleVin = vin,
        Source = source,
        Url = $"https://example.com/{source}/{vin}",
        FirstSeen = RunTime,
        LastSeen = RunTime,
        ShippingFee = shippingFee,
        PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = price, ObservedAt = RunTime }],
        Attributes = [.. attributes.Select(a => new PostingAttributeEntity { PostingId = 0, Name = a.Name, Value = a.Value, ObservedRunId = 1 })],
    };

    private static VehicleEntity Vehicle(string vin, int year, string model, int mileage, params PostingEntity[] postings) => new()
    {
        Vin = vin,
        Year = year,
        Make = "Toyota",
        Model = model,
        Mileage = mileage,
        FirstSeen = RunTime,
        LastSeen = RunTime,
        Postings = [.. postings],
    };

    private static (string Name, string Value) Deal(string value) => (PostingAttributeNames.Deal, value);

    private static (string Name, string Value) Rating(string value) => (PostingAttributeNames.DealerRating, value);

    /// <summary>Four rankable vehicles from three sites, priced so the ranking is not the ledger's
    /// own order, with the badges the walk records; <paramref name="withBadges"/> false gives the same
    /// ledger with every attribute left out.</summary>
    private static List<VehicleEntity> FixtureLedger(bool withBadges)
    {
        (string, string)[] Only(params (string, string)[] attributes) => withBadges ? attributes : [];

        return
        [
            Vehicle("4T1G11AK0LU000001", 2021, "Corolla Hybrid", 30000,
                Posting("cars.com", "4T1G11AK0LU000001", 24000m, null, Only(Deal("Great Deal"), (PostingAttributeNames.Demand, "High Demand"), Rating("4.9")))),
            Vehicle("4T1G11AK0LU000002", 2020, "Prius", 42000,
                Posting("autotrader", "4T1G11AK0LU000002", 19000m, null, Only(Deal("Good Price"), (PostingAttributeNames.Paperwork, "Online Paperwork")))),
            Vehicle("4T1G11AK0LU000003", 2022, "Prius", 21000,
                Posting("carvana", "4T1G11AK0LU000003", 20000m, 690m, Only(Deal("Great Deal"), (PostingAttributeNames.Shipping, "Free shipping")))),
            Vehicle("4T1G11AK0LU000004", 2019, "Corolla Hybrid", 55000,
                Posting("cars.com", "4T1G11AK0LU000004", 17000m, null, Only(Rating("3.3")))),
        ];
    }

    private static List<Score> Rank(IEnumerable<VehicleEntity> ledger) =>
        [.. ledger.Select(v => Scorer.Score(RankCommand.ForScoring(v, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles), DaughterScenario))];

    private static string[] Render(IReadOnlyList<Score> scores)
    {
        string? original = Environment.GetEnvironmentVariable("NO_COLOR");
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");
            var console = new TestConsole();
            console.Profile.Width = 80;
            console.Profile.Capabilities.Ansi = false;

            RankRenderer.Render(console, scores, budget: null, new Dictionary<string, ResearchStatus>(), DaughterScenario.TargetMonthlyBudgets);

            return console.Output.Replace("\r\n", "\n").Split('\n');
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original);
        }
    }

    private static string[] VinOrder(string[] lines) =>
        [.. lines.Select(l => l.Trim().Split(' ')[0]).Where(w => w.StartsWith("4T1G11AK0LU", StringComparison.Ordinal))];

    private static string[] WithoutSiteField(string[] lines) =>
        [.. lines
            .Where(l => !l.TrimStart().StartsWith("site:", StringComparison.Ordinal) && !l.Contains("GrD great deal", StringComparison.Ordinal))
            .Select(l => l.Contains("  site ", StringComparison.Ordinal) ? l[..l.IndexOf("  site ", StringComparison.Ordinal)].TrimEnd() : l.TrimEnd())];

    [Fact]
    public void Rank_WithAndWithoutBadges_ComesOutInTheSameOrderWithTheSameCostFigures()
    {
        List<Score> plain = Rank(FixtureLedger(withBadges: false));
        List<Score> badged = Rank(FixtureLedger(withBadges: true));

        Assert.All(plain, s => Assert.Null(s.Vehicle.SiteBadge));
        Assert.All(badged, s => Assert.NotNull(s.Vehicle.SiteBadge));
        Assert.Equal(plain.Select(s => s.Cost), badged.Select(s => s.Cost));
        Assert.Equal(plain.Select(s => (s.Vehicle.Vin, s.Passes, s.InsuranceUnknown)), badged.Select(s => (s.Vehicle.Vin, s.Passes, s.InsuranceUnknown)));

        string[] plainLines = Render(plain);
        string[] badgedLines = Render(badged);

        Assert.Equal(4, VinOrder(plainLines).Length);
        Assert.Equal(VinOrder(plainLines), VinOrder(badgedLines));
        Assert.Equal(plainLines.Select(l => l.TrimEnd()), WithoutSiteField(badgedLines));
    }

    [Fact]
    public void Rank_ShowsTheAbbreviatedDealBadgeAndTheDealerRatingOnTheVehicleLine()
    {
        string[] lines = Render(Rank(FixtureLedger(withBadges: true)));

        Assert.Contains(lines, l => l.Contains("4T1G11AK0LU000001") && l.Contains("site GrD 4.9"));
        Assert.Contains(lines, l => l.Contains("4T1G11AK0LU000002") && l.Contains("site GP"));
        Assert.Contains(lines, l => l.Contains("4T1G11AK0LU000003") && l.Contains("site GrD"));
        Assert.Contains(lines, l => l.Contains("4T1G11AK0LU000004") && l.Contains("site 3.3"));
        Assert.All(lines, l => Assert.True(l.Length <= 80, $"line exceeded 80 columns ({l.Length}): \"{l}\""));
    }

    [Fact]
    public void Rank_APostingWithNoDealBadgeOrRatingHasNoSiteField()
    {
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[1].Postings[0].Attributes = [new PostingAttributeEntity { PostingId = 0, Name = PostingAttributeNames.Paperwork, Value = "Online Paperwork", ObservedRunId = 1 }];

        string[] lines = Render(Rank(ledger));

        Assert.DoesNotContain(lines, l => l.Contains("4T1G11AK0LU000002") && l.Contains("site"));
        Assert.Contains(lines, l => l.Contains("4T1G11AK0LU000001") && l.Contains("site GrD 4.9"));
    }

    [Fact]
    public void Rank_NoBadgesAnywhere_PrintsNoSiteLegend()
    {
        string[] lines = Render(Rank(FixtureLedger(withBadges: false)));

        Assert.DoesNotContain(lines, l => l.Contains("site"));
    }

    [Fact]
    public void Rank_WithBadges_ExplainsTheAbbreviationsAndSaysTheyAreNeverScored()
    {
        string[] lines = Render(Rank(FixtureLedger(withBadges: true)));

        Assert.Contains(lines, l => l.Contains("site:") && l.Contains("Never scored"));
        Assert.Contains(lines, l => l.Contains("GrD great deal") && l.Contains("GP good price"));
    }

    [Fact]
    public void Rank_TheSiteFieldsOfASectionLineUp()
    {
        string[] lines = Render(Rank(FixtureLedger(withBadges: true)));

        int[] columns = [.. lines.Where(l => l.Contains("  site ") && !l.Contains("site:")).Select(l => l.IndexOf("  site ", StringComparison.Ordinal))];

        Assert.Equal(4, columns.Length);
        Assert.Single(columns.Distinct());
    }

    [Fact]
    public void Rank_TheLongestNameWithTheWidestBadgeStillFitsIn80Columns()
    {
        Score real = Rank(FixtureLedger(withBadges: true))[0];
        Score score = real with { Vehicle = real.Vehicle with { Model = "Corolla Hybrid Extra Long Trim Name That Runs On And On", SiteBadge = "GrP 4.9" } };

        string[] lines = Render([score]);

        Assert.Contains(lines, l => l.Contains("site GrP 4.9"));
        Assert.All(lines, l => Assert.True(l.Length <= 80, $"line exceeded 80 columns ({l.Length}): \"{l}\""));
    }

    [Fact]
    public void ForScoring_TheBadgeBelongsToTheCheapestPostingToTakeHome()
    {
        VehicleEntity vehicle = Vehicle(
            "4T1G11AK0LU000009", 2021, "Prius", 30000,
            Posting("carvana", "4T1G11AK0LU000009", 20000m, 1590m, Deal("Great Deal")),
            Posting("autotrader", "4T1G11AK0LU000009", 21000m, null, Deal("Good Price")));

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles);

        Assert.Equal(21000m, forScoring.LowestCurrentPrice);
        Assert.Equal("GP", forScoring.SiteBadge);
    }

    [Fact]
    public void ForScoring_CheapestPostingReservedAtCarMax_CarriesTheAvailabilityNote()
    {
        VehicleEntity vehicle = Vehicle(
            "4T1G11AK0LU000009", 2021, "Prius", 30000,
            Posting("carmax", "4T1G11AK0LU000009", 20000m, null, (PostingAttributeNames.Availability, CarMaxStores.Reserved)));

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, NoCoverage, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles);

        Assert.Equal(CarMaxStores.Reserved, forScoring.Availability);
    }

    [Fact]
    public void Rank_AVehicleWithAnotherLivePostingNotReserved_ShowsTheNoteBesideTheRowAndStillRanksItFromTheLivePosting()
    {
        // The reserved carmax posting is cheaper than the untouched cars.com posting, but it can't
        // actually be bought, so it must not drive the price: the vehicle still ranks, from the same
        // $24,000 cars.com price as the unmutated fixture, with the reserved note shown beside it.
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 18000m, ObservedAt = RunTime }];
        ledger[0].Postings[0].Attributes =
        [
            .. ledger[0].Postings[0].Attributes,
            new PostingAttributeEntity { PostingId = 0, Name = PostingAttributeNames.Availability, Value = CarMaxStores.Reserved, ObservedRunId = 1 },
        ];
        ledger[0].Postings.Add(new PostingEntity
        {
            VehicleVin = ledger[0].Vin,
            Source = "cars.com",
            Url = $"https://example.com/cars.com/{ledger[0].Vin}",
            FirstSeen = RunTime,
            LastSeen = RunTime,
            PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 24000m, ObservedAt = RunTime }],
        });

        string[] plainLines = Render(Rank(FixtureLedger(withBadges: true)));
        List<Score> reservedScores = Rank(ledger);
        string[] reservedLines = Render(reservedScores);

        Assert.Equal(VinOrder(plainLines), VinOrder(reservedLines));
        Assert.Contains(CarMaxStores.Reserved, string.Concat(reservedLines));
        Assert.DoesNotContain(CarMaxStores.Reserved, string.Concat(plainLines));
        Assert.Equal(24000m, reservedScores.Single(s => s.Vehicle.Vin == ledger[0].Vin).Vehicle.LowestCurrentPrice);
    }

    [Fact]
    public void Rank_AVehicleWithOnlyAReservedCarMaxPosting_IsExcludedWithReason()
    {
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].Attributes =
        [
            .. ledger[0].Postings[0].Attributes,
            new PostingAttributeEntity { PostingId = 0, Name = PostingAttributeNames.Availability, Value = CarMaxStores.Reserved, ObservedRunId = 1 },
        ];

        List<Score> scores = Rank(ledger);
        string[] lines = Render(scores);

        Score excludedScore = scores.Single(s => s.Vehicle.Vin == ledger[0].Vin);
        Assert.False(excludedScore.Passes);
        Assert.Contains(excludedScore.FailureReasons, r => r.Contains("reserved") && r.Contains("in transit"));
        Assert.DoesNotContain(excludedScore.FailureReasons, r => r.Contains("gone"));
        Assert.Contains(lines, l => l.Contains("Ranked (3)"));
        Assert.Contains(lines, l => l.Contains("Excluded (1)"));
        // The Reasons column wraps a reason this long across several of the table's own printed
        // lines, each with its own border padding, so the words are only contiguous once that
        // border noise and the wrap's own line breaks are collapsed back to plain single spaces.
        string output = Regex.Replace(string.Join(' ', lines).Replace('│', ' '), @"\s+", " ");
        Assert.Contains(ledger[0].Vin, output);
        Assert.Contains("every posting is reserved for another buyer or in transit, not yet purchasable", output);
    }

    [Fact]
    public void Rank_AVehicleWithOnlyAnOutOfRadiusOnlyAtCarMaxPosting_IsExcludedWithReason()
    {
        // Norco, CA is nowhere near the daughter scenario's own zip, 32833 (Orlando, FL); CarMax
        // will not transfer a car like this to a nearby store, so it stays unranked (see
        // CarMaxStoresTests for the distance figures this and the sibling tests below pin).
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].ShippingFee = 0m;
        ledger[0].Postings[0].PickupLocation = "Only at Norco";

        List<Score> scores = Rank(ledger);
        string[] lines = Render(scores);

        Score excludedScore = scores.Single(s => s.Vehicle.Vin == ledger[0].Vin);
        Assert.False(excludedScore.Passes);
        Assert.Contains(excludedScore.FailureReasons, r => r.Contains("only at Norco") && r.Contains("out of radius"));
        Assert.Contains(lines, l => l.Contains("Ranked (3)"));
        Assert.Contains(lines, l => l.Contains("Excluded (1)"));
        string output = Regex.Replace(string.Join(' ', lines).Replace('│', ' '), @"\s+", " ");
        Assert.Contains(ledger[0].Vin, output);
        Assert.Contains("only at Norco, out of radius", output);
    }

    [Fact]
    public void Rank_AVehicleWithAnInRadiusOnlyAtCarMaxPosting_StaysRankedFromItsOwnPrice()
    {
        // The Orlando store sits well inside the daughter scenario's 50-mile radius of 32833, so
        // this posting is still purchasable and ranks the vehicle normally.
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].ShippingFee = 0m;
        ledger[0].Postings[0].PickupLocation = "Only at Orlando";

        List<Score> scores = Rank(ledger);
        string[] lines = Render(scores);

        Score score = scores.Single(s => s.Vehicle.Vin == ledger[0].Vin);
        Assert.True(score.Passes);
        Assert.Equal(24000m, score.Vehicle.LowestCurrentPrice);
        Assert.Contains(lines, l => l.Contains("Ranked (4)"));
    }

    [Fact]
    public void Rank_AVehicleWithAnotherPurchasablePostingBesideAnOutOfRadiusOnlyAtOne_StaysRankedFromTheOtherPosting()
    {
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].ShippingFee = 0m;
        ledger[0].Postings[0].PickupLocation = "Only at Norco";
        ledger[0].Postings[0].PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 18000m, ObservedAt = RunTime }];
        ledger[0].Postings.Add(new PostingEntity
        {
            VehicleVin = ledger[0].Vin,
            Source = "cars.com",
            Url = $"https://example.com/cars.com/{ledger[0].Vin}",
            FirstSeen = RunTime,
            LastSeen = RunTime,
            PriceObservations = [new PriceObservationEntity { PostingId = 0, Price = 24000m, ObservedAt = RunTime }],
        });

        List<Score> scores = Rank(ledger);
        string[] lines = Render(scores);

        Score score = scores.Single(s => s.Vehicle.Vin == ledger[0].Vin);
        Assert.True(score.Passes);
        Assert.Equal(24000m, score.Vehicle.LowestCurrentPrice);
        Assert.Contains(lines, l => l.Contains("Ranked (4)"));
    }

    [Fact]
    public void Rank_AVehicleWhoseReservationClears_IsRankedAgainWithNoManualStep()
    {
        List<VehicleEntity> ledger = FixtureLedger(withBadges: true);
        ledger[0].Postings[0].Source = "carmax";
        ledger[0].Postings[0].Attributes = [];
        ledger[0].Postings[0].AvailabilityClearedAt = RunTime;

        List<Score> scores = Rank(ledger);

        Score clearedScore = scores.Single(s => s.Vehicle.Vin == ledger[0].Vin);
        Assert.True(clearedScore.Passes);
        Assert.Null(clearedScore.Vehicle.Availability);
    }

    [Fact]
    public void ForScoring_AVehicleWithNoActivePostingHasNoBadge()
    {
        VehicleEntity vehicle = Vehicle("4T1G11AK0LU000009", 2021, "Prius", 30000, Posting("carvana", "4T1G11AK0LU000009", 20000m, null, Deal("Great Deal")));
        var covered = new Dictionary<string, DateTimeOffset> { [RunSources.Key("carvana", "Prius")] = RunTime.AddDays(1) };

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, covered, Fulfillment.Delivery, DaughterScenario.Zip, DaughterScenario.RadiusMiles);

        Assert.Null(forScoring.LowestCurrentPrice);
        Assert.Null(forScoring.SiteBadge);
    }

    [Theory]
    [InlineData("Great Deal", "4.9", "GrD 4.9")]
    [InlineData("Good Deal", null, "GD")]
    [InlineData("Fair Deal", "3.3", "FD 3.3")]
    [InlineData("Great Price", null, "GrP")]
    [InlineData("Good Price", null, "GP")]
    [InlineData(null, "4.6", "4.6")]
    [InlineData("Some New Badge", null, null)]
    [InlineData(null, null, null)]
    public void SiteBadgeText_AbbreviatesTheDealAndAppendsTheRating(string? deal, string? rating, string? expected)
    {
        List<PostingAttributeEntity> attributes = [];
        if (deal is not null)
        {
            attributes.Add(new PostingAttributeEntity { PostingId = 0, Name = PostingAttributeNames.Deal, Value = deal, ObservedRunId = 1 });
        }

        if (rating is not null)
        {
            attributes.Add(new PostingAttributeEntity { PostingId = 0, Name = PostingAttributeNames.DealerRating, Value = rating, ObservedRunId = 1 });
        }

        Assert.Equal(expected, SiteBadgeText.For(attributes));
    }
}
