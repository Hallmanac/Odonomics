using Odonomics.Extraction;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the walk recovers a real hybrid the extraction left with a blank model: the recorded
/// carvana page walks/carvana/20260927-121206/camry-hybrid/detail-34.txt, titled "2026 Toyota Camry SE
/// Sedan 4D" with breadcrumb "Toyota / Camry" and body text naming a "Hybrid engine", whose extraction
/// read it as "2026 Toyota  SE, fuel type Hybrid" (walk run 6, notes/walk-2026-09-27-carvana.txt). Before
/// the fix the blank model failed the base-model check on every match branch, and the drop line falsely
/// claimed no Hybrid text existed anywhere on the page even though the extraction's own fuel type said
/// so.</summary>
public class CarvanaWalkTests
{
    private static ListingQuery CamryHybrid(int? hybridOnlyFromModelYear = null) =>
        new("Toyota", "Camry Hybrid", YearMin: 2018, "32114", 50, 100000, hybridOnlyFromModelYear);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void RecordedBlankModelHybridPage_IsResolvedAndSavedAsTheCamryHybrid()
    {
        string pageText = Fixture("carvana-detail-camry-hybrid-blank-model.txt");
        ListingQuery query = CamryHybrid();

        string? model = WalkSites.Carvana.ResolveModel(null, "Toyota", "SE", 2026, pageText);

        Assert.Equal("Camry", model);
        Assert.True(query.MatchesWalkedPage("Toyota", model, "SE", 2026, pageText, "Hybrid"));
    }

    [Fact]
    public void RecordedBlankModelPage_TitleNamingAnotherModel_StillDroppedWithTheNewReason()
    {
        // Same shape (blank model, fuel type Hybrid) but the page's own title names a different
        // model, so the title-line fallback resolves to that other model rather than to Camry, and the
        // page is correctly still rejected: a title fallback recovers what the page actually says, not
        // whatever model the query was hoping for.
        string pageText = Fixture("carvana-detail-camry-hybrid-blank-model.txt")
            .Replace("2026 Toyota Camry\nSE Sedan 4D", "2026 Toyota Highlander\nSE Sedan 4D");
        ListingQuery query = CamryHybrid();

        string? model = WalkSites.Carvana.ResolveModel(null, "Toyota", "SE", 2026, pageText);

        Assert.Equal("Highlander", model);
        Assert.False(query.MatchesWalkedPage("Toyota", model, "SE", 2026, pageText, "Hybrid"));
        Assert.Equal(
            "doesn't match Toyota Camry Hybrid: 2026 Toyota Highlander SE",
            WalkOutcomeWording.NotMatchingDetail(query, new ExtractionResult(
                Vin: "4T1DAACK8TU240486", Year: 2026, Make: "Toyota", Model: model, Trim: "SE",
                Price: 33990m, Mileage: 12066, DealerName: null, DealerLocation: null, FuelType: "Hybrid")));
    }

    [Fact]
    public void RecordedBlankModelPage_TitleNamingNoModelAtAll_StaysBlankAndReadsAsMissing()
    {
        // A title the make itself doesn't appear on (a make mismatch, or a page whose title line was
        // stripped) leaves the fallback with nothing to resolve, so the model stays blank and the drop
        // reason says so plainly rather than printing a hollow "doesn't match ... :  " comparison.
        string pageText = Fixture("carvana-detail-camry-hybrid-blank-model.txt");
        ListingQuery query = CamryHybrid();

        string? model = WalkSites.Carvana.ResolveModel(null, "Honda", "SE", 2026, pageText);

        Assert.Null(model);
        Assert.Equal(
            "model missing from extraction",
            WalkOutcomeWording.NotMatchingDetail(query, new ExtractionResult(
                Vin: "4T1DAACK8TU240486", Year: 2026, Make: "Honda", Model: model, Trim: "SE",
                Price: 33990m, Mileage: 12066, DealerName: null, DealerLocation: null, FuelType: "Hybrid")));
    }
}
