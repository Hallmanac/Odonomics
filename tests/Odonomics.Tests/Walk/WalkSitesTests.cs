using System.Text;
using System.Text.Json;
using Odonomics.Domain;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkSitesTests
{
    private static ListingQuery Query(string make, string model, string zip = "32114", int? hybridOnlyFromModelYear = null, int yearMin = 2019, int maxMileage = 100000) =>
        new(make, model, yearMin, zip, 50, maxMileage, hybridOnlyFromModelYear);

    private static Scenario Shipped() => ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    [Fact]
    public void CarsCom_BuildSearchUrl_UsesMakeModelSlugZipRadiusYearAndMileage()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Corolla"));

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla&zip=32114&maximum_distance=50&year_min=2019&mileage_max=100000",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_CorollaHybrid_MatchesOperatorsBrowserUrl()
    {
        // The exact facet value Brian's Edge browser built ticking "Corolla Hybrid"
        // (notes/run-session-2026-09-22.md, "Facet URLs from Brian"), minus its sid and sort.
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Corolla Hybrid", zip: "32833"));

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla_hybrid&zip=32833&maximum_distance=50&year_min=2019&mileage_max=100000",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_CamryHybrid_UnderscoresHybridWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Camry Hybrid"));

        Assert.Contains("models[]=toyota-camry_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_MultiWordNonHybridModel_UnderscoresEveryWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Corolla Cross"));

        Assert.Contains("models[]=toyota-corolla_cross&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HyphenatedModel_UnderscoresTheHyphenToo()
    {
        // cars.com's own model-facet JSON carries "honda-cr_v_hybrid"
        // (spike/recorded/cars.com/day1/Honda-Insight-search.html) and, for the same
        // hyphen-collapsing rule, "toyota-c_hr"
        // (spike/recorded/cars.com/day1/Toyota-Corolla_Hybrid-search.html), not the hyphenated
        // "honda-cr-v_hybrid" a plain space-to-underscore replace would produce.
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Honda", "CR-V Hybrid"));

        Assert.Contains("models[]=honda-cr_v_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HondaInsight_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Honda", "Insight"));

        Assert.Contains("models[]=honda-insight&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_ToyotaPrius_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Prius"));

        Assert.Contains("models[]=toyota-prius&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_HybridOnlyFromModelYear_QueriesBothBaseAndHybridFacets()
    {
        // Toyota's 2025+ Camry is hybrid-only but cars.com never moved those listings into the
        // "camry_hybrid" bucket (they're still filed under plain "camry"); the operator's own
        // recorded search.txt for the "toyota-camry" bucket lists Camrys from 2010 through 2024
        // with zero case-insensitive "hybrid" occurrences. Querying only the hybrid facet for a
        // HybridOnlyFromModelYear model would silently exclude every post-cutover listing, but
        // querying only the base facet (the walk's prior behavior) would silently exclude every
        // pre-cutover Camry Hybrid that cars.com genuinely does file under "camry_hybrid". Since
        // models[] is a checkbox array, the walk asks for both buckets and leaves
        // ListingQuery.MatchesExtractedVehicle to accept the base-titled ones at or after the
        // hybrid year and reject the genuinely-gas ones below it.
        string url = WalkSites.CarsCom.BuildSearchUrl(Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025));

        Assert.Contains("models[]=toyota-camry&", url);
        Assert.Contains("models[]=toyota-camry_hybrid&", url);
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
        string url = WalkSites.Carvana.BuildSearchUrl(Query("Honda", "Insight"));

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
        string url = WalkSites.Carvana.BuildSearchUrl(Query("Toyota", "Corolla Hybrid"));

        JsonElement filters = DecodeCvnaid(url);
        JsonElement makes = filters.GetProperty("filters").GetProperty("makes");
        Assert.Equal("Toyota", makes[0].GetProperty("name").GetString());
        Assert.Equal("Corolla", makes[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
        Assert.Equal("Hybrid", filters.GetProperty("filters").GetProperty("fuelTypes")[0].GetString());
    }

    [Fact]
    public void Carvana_BuildSearchUrl_Prius_MatchesTheOrchestratorsVerifiedUrl()
    {
        // The whole URL here, route and zip included, is the one the orchestrator loaded over CDP
        // in Brian's Edge and confirmed carvana honored (notes/run-session-2026-09-22.md, "Year
        // and mileage facet URLs, verified by the orchestrator"): the chips read "2019 or newer"
        // and "Under 100,000 miles". The key is the singular "year"; a "years" key is ignored.
        string url = WalkSites.Carvana.BuildSearchUrl(Query("Toyota", "Prius"));

        Assert.Equal(
            "https://www.carvana.com/cars/filters?zip=32114&cvnaid=" +
            "eyJmaWx0ZXJzIjp7Im1ha2VzIjpbeyJuYW1lIjoiVG95b3RhIiwicGFyZW50TW9kZWxzIjpbeyJuYW1lIjoiUHJpdXMifV19XSwieWVhciI6eyJtaW4iOjIwMTl9LCJtaWxlYWdlIjp7Im1heCI6MTAwMDAwfX19",
            url);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_CvnaidIsUnpaddedBase64UrlOfTheFilterJson()
    {
        string url = WalkSites.Carvana.BuildSearchUrl(Query("Toyota", "Corolla Hybrid"));

        string cvnaid = url[(url.IndexOf("cvnaid=", StringComparison.Ordinal) + "cvnaid=".Length)..];
        Assert.DoesNotContain('=', cvnaid);
        Assert.DoesNotContain('+', cvnaid);
        Assert.DoesNotContain('/', cvnaid);
        Assert.Equal(
            """{"filters":{"makes":[{"name":"Toyota","parentModels":[{"name":"Corolla"}]}],"fuelTypes":["Hybrid"],"year":{"min":2019},"mileage":{"max":100000}}}""",
            DecodeCvnaid(url).ToString());
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_ReadsYearAndMileageFromTheScenario()
    {
        Scenario scenario = Shipped();

        string prius = WalkSites.CarsCom.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Prius"));
        string camry = WalkSites.CarsCom.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Camry Hybrid"));

        Assert.EndsWith("&year_min=2019&mileage_max=100000", prius);
        Assert.EndsWith("&year_min=2018&mileage_max=100000", camry);
    }

    [Fact]
    public void CarsCom_BuildSearchUrl_FollowsAChangedScenario()
    {
        Scenario scenario = Shipped() with
        {
            Filters = new HardFilters
            {
                MinModelYear = 2021,
                MinModelYearOverrides = new Dictionary<string, int> { ["Toyota Camry Hybrid"] = 2020 },
                MaxMileage = 65000,
                AllowedModels = ["Toyota Prius", "Toyota Camry Hybrid"],
            },
        };

        string prius = WalkSites.CarsCom.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Prius"));
        string camry = WalkSites.CarsCom.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Camry Hybrid"));

        Assert.EndsWith("&year_min=2021&mileage_max=65000", prius);
        Assert.EndsWith("&year_min=2020&mileage_max=65000", camry);
    }

    [Fact]
    public void Carvana_BuildSearchUrl_ReadsYearAndMileageFromTheScenario_IncludingTheHybridOnlyCamry()
    {
        Scenario scenario = Shipped();

        JsonElement prius = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Prius"))).GetProperty("filters");
        JsonElement camry = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Camry Hybrid"))).GetProperty("filters");

        Assert.Equal(2019, prius.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(100000, prius.GetProperty("mileage").GetProperty("max").GetInt32());
        Assert.Equal(2018, camry.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(100000, camry.GetProperty("mileage").GetProperty("max").GetInt32());
        Assert.Equal("Hybrid", camry.GetProperty("fuelTypes")[0].GetString());
        Assert.Equal("Camry", camry.GetProperty("makes")[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Carvana_BuildSearchUrl_FollowsAChangedScenario()
    {
        Scenario scenario = Shipped() with
        {
            Filters = new HardFilters
            {
                MinModelYear = 2021,
                MinModelYearOverrides = new Dictionary<string, int>(),
                MaxMileage = 65000,
                AllowedModels = ["Toyota Prius"],
            },
        };

        JsonElement filters = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrl(ListingQuery.For(scenario, "Toyota Prius"))).GetProperty("filters");

        Assert.Equal(2021, filters.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(65000, filters.GetProperty("mileage").GetProperty("max").GetInt32());
    }

    [Fact]
    public void Carvana_BuildSearchUrl_HybridOnlyFromModelYear_StillFiltersByFuelType()
    {
        // Unlike cars.com, carvana's fuelTypes filter matches each listing's actual fuel type
        // rather than its title text, so a hybrid-only-from-year model needs no base-model
        // fallback: it already returns those listings under the normal hybrid query.
        string withFlag = WalkSites.Carvana.BuildSearchUrl(Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025));
        string withoutFlag = WalkSites.Carvana.BuildSearchUrl(Query("Toyota", "Camry Hybrid"));

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
