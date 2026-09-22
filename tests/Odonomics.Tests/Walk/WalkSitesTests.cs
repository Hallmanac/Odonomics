using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkSitesTests
{
    /// <summary>A slice of a real cars.com Toyota search page's own model-facet data (recorded
    /// 2026-09-22, trimmed to the facet array), used to prove the search-URL builder against the
    /// exact models[] value cars.com itself expects rather than a guessed slug shape.</summary>
    private static readonly string CarsComModelFacetsFixture = File.ReadAllText(
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walk", "cars-com", "toyota-search-model-facets.html"));

    private static string ExpectedCarsComModelValue(string modelName)
    {
        Match match = Regex.Match(CarsComModelFacetsFixture, $"\"name\":\"{Regex.Escape(modelName)}\",\"value\":\"(?<value>[a-z0-9_-]+)\"");
        Assert.True(match.Success, $"fixture does not carry a facet entry for \"{modelName}\"");
        return match.Groups["value"].Value;
    }

    [Theory]
    [InlineData("Corolla")]
    [InlineData("Corolla Hybrid")]
    [InlineData("Camry")]
    [InlineData("Camry Hybrid")]
    [InlineData("Prius")]
    [InlineData("Prius Prime")]
    public void CarsCom_BuildSearchUrl_UsesTheSitesOwnModelFacetValue(string model)
    {
        string expectedModelsValue = ExpectedCarsComModelValue(model);

        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", model, "32114", 50);

        Assert.Contains("makes[]=toyota", url);
        Assert.Contains($"models[]={expectedModelsValue}", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_SeparatesHybridFromItsBaseModel()
    {
        string hybridUrl = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla Hybrid", "32114", 50);
        string baseUrl = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla", "32114", 50);

        Assert.NotEqual(baseUrl, hybridUrl);
        Assert.Contains("models[]=toyota-corolla_hybrid", hybridUrl);
        Assert.Contains("models[]=toyota-corolla&", baseUrl);
    }

    [Fact]
    public void CarsCom_DetailUrlPattern_MatchesVehicleDetailLinks()
    {
        Assert.Matches(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/vehicledetail/abc123/");
        Assert.DoesNotMatch(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/shopping/results/");
    }

    [Fact]
    public void CarsCom_DetailLinkOverfetchMultiplier_IsOne()
    {
        Assert.Equal(1, WalkSites.CarsCom.DetailLinkOverfetchMultiplier);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_UsesMakeModelSlugAndZip()
    {
        string url = WalkSites.Carvana.BuildSearchUrl("Honda", "Insight", "32114", 50);

        Assert.Equal("https://www.carvana.com/cars/honda-insight?zip=32114", url);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_StripsHybridSuffixSinceNoSeparateFacetIsConfirmed()
    {
        string url = WalkSites.Carvana.BuildSearchUrl("Toyota", "Corolla Hybrid", "32114", 50);

        Assert.Equal("https://www.carvana.com/cars/toyota-corolla?zip=32114", url);
    }

    [Fact]
    public void Carvana_DetailLinkOverfetchMultiplier_IsGreaterThanOne()
    {
        Assert.True(WalkSites.Carvana.DetailLinkOverfetchMultiplier > 1);
    }

    [Fact]
    public void Carvana_DetailUrlPattern_MatchesVehicleLinks()
    {
        Assert.Matches(WalkSites.Carvana.DetailUrlPattern, "https://www.carvana.com/vehicle/4754913");
    }

    [Theory]
    [InlineData("cars.com")]
    [InlineData("carvana")]
    [InlineData("CARS.COM")]
    public void Find_KnownSite_ReturnsIt(string name)
    {
        Assert.NotNull(WalkSites.Find(name));
    }

    [Fact]
    public void Find_UnknownSite_ReturnsNull()
    {
        Assert.Null(WalkSites.Find("autotrader"));
    }
}
