using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkSitesTests
{
    [Fact]
    public void CarsCom_BuildSearchUrl_StripsHybridSuffixFromModelSlug()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla Hybrid", "32114", 50);

        Assert.Contains("makes[]=toyota", url);
        Assert.Contains("models[]=toyota-corolla", url);
        Assert.DoesNotContain("hybrid", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CarsCom_DetailUrlPattern_MatchesVehicleDetailLinks()
    {
        Assert.Matches(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/vehicledetail/abc123/");
        Assert.DoesNotMatch(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/shopping/results/");
    }

    [Fact]
    public void Carvana_BuildSearchUrl_UsesMakeModelSlugAndZip()
    {
        string url = WalkSites.Carvana.BuildSearchUrl("Honda", "Insight", "32114", 50);

        Assert.Equal("https://www.carvana.com/cars/honda-insight?zip=32114", url);
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
