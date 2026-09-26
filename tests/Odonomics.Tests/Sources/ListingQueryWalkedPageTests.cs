using Odonomics.Sources;

namespace Odonomics.Tests.Sources;

/// <summary>Proves the walk's page-text backstop over recorded Carvana detail pages: two used
/// Corolla Hybrids titled "2023 Toyota Corolla Hybrid" and "2020 Toyota Corolla Hybrid" that the
/// extraction split into model "Corolla" and trim "LE", and a gas 2020 Corolla LE.</summary>
public class ListingQueryWalkedPageTests
{
    private static ListingQuery Query(string make, string model) =>
        new(make, model, YearMin: 2019, "32114", 50, MaxMileage: 100000);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Theory]
    [InlineData("carvana-detail-corolla-hybrid-2023.txt", 2023)]
    [InlineData("carvana-detail-corolla-hybrid-2020.txt", 2020)]
    public void MatchesWalkedPage_HybridTitleSplitIntoBaseModelAndTrim_Matches(string fixture, int year)
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Corolla", "LE", year));
        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla", "LE", year, Fixture(fixture)));
    }

    [Fact]
    public void MatchesWalkedPage_GasCorollaPage_StillRejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2020, Fixture("carvana-detail-gas-corolla.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_GasCorollaPageMentioningAHybridElsewhere_StillRejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");
        string page = Fixture("carvana-detail-gas-corolla.txt")
            + "\nWhat is the real-world fuel economy for the Corolla Hybrid?\n2020 Toyota Corolla Hybrid\n";

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2020, page));
    }

    [Fact]
    public void MatchesWalkedPage_TitleHybridButYearDiffers_Rejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2021, Fixture("carvana-detail-corolla-hybrid-2020.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_HybridTitleButOtherMake_Rejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesWalkedPage("Honda", "Corolla", "LE", 2023, Fixture("carvana-detail-corolla-hybrid-2023.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_HybridTitleButOtherBaseModel_Rejected()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2023, Fixture("carvana-detail-corolla-hybrid-2023.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_NonHybridQuery_TitleCheckNeverApplies()
    {
        ListingQuery query = Query("Toyota", "Corolla");

        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2023, Fixture("carvana-detail-corolla-hybrid-2023.txt")));
        Assert.False(query.MatchesWalkedPage("Toyota", "Camry", "LE", 2023, Fixture("carvana-detail-corolla-hybrid-2023.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_ExtractionAlreadyMatches_MatchesWithoutTheTitle()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla Hybrid", "LE", 2023, "no title here"));
    }

    [Theory]
    [InlineData("autotrader-detail-corolla-hybrid-spec-1.txt", 2026)]
    [InlineData("autotrader-detail-corolla-hybrid-spec-5.txt", 2023)]
    [InlineData("autotrader-detail-corolla-hybrid-spec-17.txt", 2024)]
    public void MatchesWalkedPage_TitleSaysNoHybridButSpecLineDoes_Matches(string fixture, int year)
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Corolla", "LE", year));
        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla", "LE", year, Fixture(fixture)));
    }

    [Fact]
    public void MatchesWalkedPage_SpecLineHybridButOtherBaseModel_Rejected()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2026, Fixture("autotrader-detail-corolla-hybrid-spec-1.txt")));
    }

    [Fact]
    public void MatchesWalkedPage_SpecLineHybridOnNonHybridQuery_DoesNotChangeTheMatch()
    {
        ListingQuery query = Query("Toyota", "Corolla");

        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2026, Fixture("autotrader-detail-corolla-hybrid-spec-1.txt")));
    }

    [Theory]
    [InlineData("Hybrid: Gas/Electric")]
    [InlineData("hybrid: gas/electric")]
    [InlineData("  Fuel Type: Hybrid")]
    [InlineData("Fuel: Hybrid")]
    [InlineData("Engine: 1.8L 4-Cylinder Hybrid")]
    public void MatchesWalkedPage_EachSpecLineForm_Matches(string specLine)
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.True(query.MatchesWalkedPage("Toyota", "Camry", "LE", 2024, $"Used 2024 Toyota Camry LE\nSpecs\n{specLine}\nAutomatic"));
    }

    [Theory]
    [InlineData("Fuel Type: Gasoline")]
    [InlineData("Fuel Type: Plug-in Hybrid")]
    [InlineData("Engine: 2.5L 4-Cylinder")]
    [InlineData("Not a Hybrid: Gas/Electric")]
    [InlineData("Gas")]
    public void MatchesWalkedPage_SpecLinesThatDoNotSayHybrid_Rejected(string specLine)
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.False(query.MatchesWalkedPage("Toyota", "Camry", "LE", 2024, $"Used 2024 Toyota Camry LE\nSpecs\n{specLine}\nAutomatic"));
    }

    [Theory]
    [InlineData("Similar Vehicles")]
    [InlineData("Check out similar styles from this dealer")]
    [InlineData("Recommended Cars")]
    public void MatchesWalkedPage_SpecLineOnlyBelowASimilarVehiclesHeading_Rejected(string heading)
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");
        string page = $"Used 2020 Toyota Corolla LE\nSpecs\nGasoline\n{heading}\nUsed 2022 Toyota Corolla LE\nHybrid: Gas/Electric\n";

        Assert.False(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2020, page));
    }

    [Fact]
    public void MatchesWalkedPage_ViewSimilarVehiclesButtonAboveTheSpecs_DoesNotHideTheSpecLine()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.Contains("View similar vehicles", Fixture("autotrader-detail-corolla-hybrid-spec-1.txt"));
        Assert.True(query.MatchesWalkedPage("Toyota", "Corolla", "LE", 2026, Fixture("autotrader-detail-corolla-hybrid-spec-1.txt")));
    }
}
