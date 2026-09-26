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
}
