using System.Text;
using System.Text.Json;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkSitesTests
{
    [Fact]
    public void CarsCom_BuildSearchUrl_UsesMakeModelSlugZipAndRadius()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla", "32114", 50, false);

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla&zip=32114&maximum_distance=50",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_CorollaHybrid_MatchesOperatorsBrowserUrl()
    {
        // The exact facet value Brian's Edge browser built ticking "Corolla Hybrid"
        // (notes/run-session-2026-09-22.md, "Facet URLs from Brian"), minus its sid and sort.
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla Hybrid", "32833", 50, false);

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla_hybrid&zip=32833&maximum_distance=50",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_CamryHybrid_UnderscoresHybridWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Camry Hybrid", "32114", 50, false);

        Assert.Contains("models[]=toyota-camry_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_MultiWordNonHybridModel_UnderscoresEveryWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Corolla Cross", "32114", 50, false);

        Assert.Contains("models[]=toyota-corolla_cross&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HyphenatedModel_UnderscoresTheHyphenToo()
    {
        // cars.com's own model-facet JSON carries "honda-cr_v_hybrid" and "toyota-c_hr", not the
        // hyphenated "honda-cr-v_hybrid" a plain space-to-underscore replace would produce
        // (spike/recorded/cars.com/day1/Honda-Insight-search.html).
        string url = WalkSites.CarsCom.BuildSearchUrl("Honda", "CR-V Hybrid", "32114", 50, false);

        Assert.Contains("models[]=honda-cr_v_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HondaInsight_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Honda", "Insight", "32114", 50, false);

        Assert.Contains("models[]=honda-insight&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_ToyotaPrius_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Prius", "32114", 50, false);

        Assert.Contains("models[]=toyota-prius&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HybridOnlyFromModelYear_QueriesBaseModelNotHybridFacet()
    {
        // Toyota's 2025+ Camry is hybrid-only but cars.com never moved those listings into the
        // "camry_hybrid" bucket (they're still filed under plain "camry"); this run's own
        // recorded search.txt for the "toyota-camry" bucket lists eleven 2025/2026 Camrys with
        // "Hybrid" nowhere in their titles. Querying the hybrid facet for a HybridOnlyFromModelYear
        // model would silently exclude all of them, so the walk queries the base model instead and
        // leaves ListingQuery.MatchesExtractedVehicle to accept the ones at or after the hybrid
        // year.
        string url = WalkSites.CarsCom.BuildSearchUrl("Toyota", "Camry Hybrid", "32114", 50, true);

        Assert.Contains("models[]=toyota-camry&", url);
        Assert.DoesNotContain("camry_hybrid", url);
    }

    [Fact]
    public void CarsCom_DetailUrlPattern_MatchesVehicleDetailLinks()
    {
        Assert.Matches(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/vehicledetail/abc123/");
        Assert.DoesNotMatch(WalkSites.CarsCom.DetailUrlPattern, "https://www.cars.com/shopping/results/");
    }

    [Fact]
    public void CarsCom_DetailLinkOverfetchMultiplier_IsTwo()
    {
        Assert.Equal(2, WalkSites.CarsCom.DetailLinkOverfetchMultiplier);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_NonHybridModel_HasNoFuelTypesFilter()
    {
        string url = WalkSites.Carvana.BuildSearchUrl("Honda", "Insight", "32114", 50, false);

        JsonElement filters = DecodeCvnaid(url);
        JsonElement makes = filters.GetProperty("filters").GetProperty("makes");
        Assert.Equal("Honda", makes[0].GetProperty("name").GetString());
        Assert.Equal("Insight", makes[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
        Assert.False(filters.GetProperty("filters").TryGetProperty("fuelTypes", out _));
        Assert.StartsWith("https://www.carvana.com/cars/filters?zip=32114&cvnaid=", url);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_HybridModel_FiltersByBaseModelAndFuelType()
    {
        string url = WalkSites.Carvana.BuildSearchUrl("Toyota", "Corolla Hybrid", "32114", 50, false);

        JsonElement filters = DecodeCvnaid(url);
        JsonElement makes = filters.GetProperty("filters").GetProperty("makes");
        Assert.Equal("Toyota", makes[0].GetProperty("name").GetString());
        Assert.Equal("Corolla", makes[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
        Assert.Equal("Hybrid", filters.GetProperty("filters").GetProperty("fuelTypes")[0].GetString());
    }

    [Fact]
    public void Carvana_BuildSearchUrl_CorollaHybrid_CvnaidMatchesOperatorsBrowserValue()
    {
        // The whole URL here, route and zip included, is attested against Brian's Edge browser
        // (notes/run-session-2026-09-22.md, "Facet URLs from Brian"): that note's pasted carvana
        // URL is exactly "https://www.carvana.com/cars/filters?zip=32114&cvnaid=...", so neither
        // the route nor the zip parameter is a guess carried over from the walk's old URL shape.
        string url = WalkSites.Carvana.BuildSearchUrl("Toyota", "Corolla Hybrid", "32114", 50, false);

        Assert.Equal(
            "https://www.carvana.com/cars/filters?zip=32114&cvnaid=" +
            "eyJmaWx0ZXJzIjp7Im1ha2VzIjpbeyJuYW1lIjoiVG95b3RhIiwicGFyZW50TW9kZWxzIjpbeyJuYW1lIjoiQ29yb2xsYSJ9XX1dLCJmdWVsVHlwZXMiOlsiSHlicmlkIl19fQ",
            url);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_HybridOnlyFromModelYear_StillFiltersByFuelType()
    {
        // Unlike cars.com, carvana's fuelTypes filter matches each listing's actual fuel type
        // rather than its title text, so a hybrid-only-from-year model needs no base-model
        // fallback: it already returns those listings under the normal hybrid query.
        string withFlag = WalkSites.Carvana.BuildSearchUrl("Toyota", "Camry Hybrid", "32114", 50, true);
        string withoutFlag = WalkSites.Carvana.BuildSearchUrl("Toyota", "Camry Hybrid", "32114", 50, false);

        Assert.Equal(withoutFlag, withFlag);
    }

    [Fact]
    public void Carvana_DetailLinkOverfetchMultiplier_IsTwo()
    {
        Assert.Equal(2, WalkSites.Carvana.DetailLinkOverfetchMultiplier);
    }

    [Fact]
    public void Carvana_DetailUrlPattern_MatchesVehicleLinks()
    {
        Assert.Matches(WalkSites.Carvana.DetailUrlPattern, "https://www.carvana.com/vehicle/4754913");
    }

    private static JsonElement DecodeCvnaid(string url)
    {
        string cvnaid = url[(url.IndexOf("cvnaid=", StringComparison.Ordinal) + "cvnaid=".Length)..];
        string base64 = cvnaid.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return JsonDocument.Parse(json).RootElement;
    }

    [Theory]
    [InlineData(
        "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/?attribution_type=p_one&sid=41a85986-4b8e-4bfc-ae1a-9925e046534a",
        "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/")]
    [InlineData(
        "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/?openLeadForm=true&sid=41a85986-4b8e-4bfc-ae1a-9925e046534a",
        "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/")]
    [InlineData(
        "https://www.carvana.com/vehicle/4754913?refSource=srp",
        "https://www.carvana.com/vehicle/4754913")]
    public void CanonicalDetailUrl_StripsQueryString(string href, string expected)
    {
        Assert.Equal(expected, WalkSites.CanonicalDetailUrl(href));
    }

    [Fact]
    public void CanonicalDetailUrl_SameVehicleDifferentQueryVariants_ProduceSameCanonicalUrl()
    {
        string withSidOnly = WalkSites.CanonicalDetailUrl(
            "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/?sid=41a85986-4b8e-4bfc-ae1a-9925e046534a");
        string withOpenLeadForm = WalkSites.CanonicalDetailUrl(
            "https://www.cars.com/vehicledetail/84da298f-4e73-4ea4-98d6-d1007f21ffb5/?openLeadForm=true&sid=c963e20c-ed97-4844-97ec-3483c73cda34");

        Assert.Equal(withSidOnly, withOpenLeadForm);
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
