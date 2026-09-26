using Odonomics.Cli;
using Odonomics.Cli.Commands;
using Odonomics.Domain;
using Odonomics.Ledger;
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
                Posting("carvana", "4T1G11AK0LU000003", 26000m, 690m, Only(Deal("Great Deal"), (PostingAttributeNames.Shipping, "Free shipping")))),
            Vehicle("4T1G11AK0LU000004", 2019, "Corolla Hybrid", 55000,
                Posting("cars.com", "4T1G11AK0LU000004", 17000m, null, Only(Rating("3.3")))),
        ];
    }

    private static List<Score> Rank(IEnumerable<VehicleEntity> ledger) =>
        [.. ledger.Select(v => Scorer.Score(RankCommand.ForScoring(v, NoCoverage), DaughterScenario))];

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

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, NoCoverage);

        Assert.Equal(21000m, forScoring.LowestCurrentPrice);
        Assert.Equal("GP", forScoring.SiteBadge);
    }

    [Fact]
    public void ForScoring_AVehicleWithNoActivePostingHasNoBadge()
    {
        VehicleEntity vehicle = Vehicle("4T1G11AK0LU000009", 2021, "Prius", 30000, Posting("carvana", "4T1G11AK0LU000009", 20000m, null, Deal("Great Deal")));
        var covered = new Dictionary<string, DateTimeOffset> { [RunSources.Key("carvana", "Prius")] = RunTime.AddDays(1) };

        VehicleForScoring forScoring = RankCommand.ForScoring(vehicle, covered);

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
