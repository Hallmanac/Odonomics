using System.Reflection;
using Odonomics.Extraction;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves what dealer a carvana posting is stored with, from the extraction step's
/// output through <see cref="WalkSite.ResolveDealerName"/>. The extraction call itself needs a live
/// model, so these tests start from what it returned and run it through the same grounding check
/// (ExtractionClient.GroundInPageText, by reflection as in ExtractionGroundingTests) against a
/// recorded page. The no-dealer page is a real walk capture (carvana, camry-hybrid, 2026-09-23):
/// like every carvana detail page recorded so far it names no hub, only "Carvana" in its footer
/// and "Orlando, FL" as a pickup location. No recorded page names a hub such as "Carvana Winder"
/// (those names only appeared in the Marketcheck VIN history), so the hub page is that same page
/// with one seller line added, and says so.</summary>
public class CarvanaDealerNameTests
{
    private const string Vin = "4T1B21HK8KU518914";

    private static readonly MethodInfo GroundInPageTextMethod = typeof(ExtractionClient)
        .GetMethod("GroundInPageText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractionClient.GroundInPageText not found");

    private static string NoDealerPage() =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", "carvana-detail-no-dealer.txt"));

    private static string HubPage() => NoDealerPage() + "\nSold by Carvana Winder\n";

    private static string StoredDealerName(string? extractedDealerName, string pageText)
    {
        var extracted = new ExtractionResult(Vin, 2019, "Toyota", "Camry", "SE", 23590m, 73094, extractedDealerName, null);
        var grounded = (ExtractionResult)GroundInPageTextMethod.Invoke(null, [extracted, pageText])!;
        return WalkSites.Carvana.ResolveDealerName(grounded.DealerName)
            ?? throw new InvalidOperationException("carvana resolved no dealer name");
    }

    [Fact]
    public void ResolveDealerName_RecordedPageNamesNoDealerAndExtractionReturnsNull_StoresCarvana()
    {
        Assert.Equal("Carvana", StoredDealerName(null, NoDealerPage()));
    }

    [Fact]
    public void ResolveDealerName_RecordedPageNamesNoDealerAndExtractionReturnsBareCarvana_StoresCarvana()
    {
        Assert.Equal("Carvana", StoredDealerName("Carvana", NoDealerPage()));
    }

    [Fact]
    public void ResolveDealerName_ExtractionInventsAHubTheRecordedPageNeverNames_FallsBackToCarvana()
    {
        // Grounding drops a dealer name that isn't literally on the page, and the fallback then
        // applies, so a fabricated hub can never be stored.
        Assert.Equal("Carvana", StoredDealerName("Carvana Winder", NoDealerPage()));
    }

    [Fact]
    public void ResolveDealerName_PageNamesAHub_KeepsTheHubName()
    {
        Assert.Equal("Carvana Winder", StoredDealerName("Carvana Winder", HubPage()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveDealerName_BlankExtractedName_StoresCarvanaOnCarvanaAndNothingOnCarsCom(string? extracted)
    {
        Assert.Equal("Carvana", WalkSites.Carvana.ResolveDealerName(extracted));
        Assert.Null(WalkSites.CarsCom.ResolveDealerName(extracted));
    }

    [Fact]
    public void ResolveDealerName_CarsComNamedDealer_IsKeptAsExtracted()
    {
        Assert.Equal("Holler Honda", WalkSites.CarsCom.ResolveDealerName("  Holler Honda "));
    }
}
