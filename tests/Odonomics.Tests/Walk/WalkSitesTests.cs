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
    public void CarsCom_BuildSearchUrls_UsesMakeModelSlugZipRadiusYearAndMileage()
    {
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Corolla")).Single();

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla&zip=32114&maximum_distance=50&year_min=2019&mileage_max=100000",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_CorollaHybrid_MatchesOperatorsBrowserUrl()
    {
        // The exact facet value Brian's Edge browser built ticking "Corolla Hybrid"
        // (notes/run-session-2026-09-22.md, "Facet URLs from Brian"), minus its sid and sort.
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Corolla Hybrid", zip: "32833")).Single();

        Assert.Equal(
            "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla_hybrid&zip=32833&maximum_distance=50&year_min=2019&mileage_max=100000",
            url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_CamryHybrid_UnderscoresHybridWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Camry Hybrid")).Single();

        Assert.Contains("models[]=toyota-camry_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_MultiWordNonHybridModel_UnderscoresEveryWord()
    {
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Corolla Cross")).Single();

        Assert.Contains("models[]=toyota-corolla_cross&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_HyphenatedModel_UnderscoresTheHyphenToo()
    {
        // cars.com's own model-facet JSON carries "honda-cr_v_hybrid"
        // (spike/recorded/cars.com/day1/Honda-Insight-search.html) and, for the same
        // hyphen-collapsing rule, "toyota-c_hr"
        // (spike/recorded/cars.com/day1/Toyota-Corolla_Hybrid-search.html), not the hyphenated
        // "honda-cr-v_hybrid" a plain space-to-underscore replace would produce.
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Honda", "CR-V Hybrid")).Single();

        Assert.Contains("models[]=honda-cr_v_hybrid&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_HondaInsight_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Honda", "Insight")).Single();

        Assert.Contains("models[]=honda-insight&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_ToyotaPrius_Unchanged()
    {
        string url = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Prius")).Single();

        Assert.Contains("models[]=toyota-prius&", url);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_HybridOnlyFromModelYear_YieldsAHybridSearchThenABaseModelSearch()
    {
        // Toyota's 2025+ Camry is hybrid-only but cars.com never moved those listings into the
        // "camry_hybrid" bucket (they're still filed under plain "camry"), so both buckets have to
        // be searched. cars.com takes one year_min per request, though, so the base model gets its
        // own search from the hybrid-only year rather than sharing the scenario's 2018 minimum,
        // which would fill the results with gas Camrys from 2018 through 2024.
        IReadOnlyList<string> urls = WalkSites.CarsCom.BuildSearchUrls(
            Query("Toyota", "Camry Hybrid", zip: "32833", hybridOnlyFromModelYear: 2025, yearMin: 2018));

        Assert.Equal(
            [
                "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-camry_hybrid&zip=32833&maximum_distance=50&year_min=2018&mileage_max=100000",
                "https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-camry&zip=32833&maximum_distance=50&year_min=2025&mileage_max=100000",
            ],
            urls);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_MinimumYearAfterTheHybridOnlyYear_BaseSearchStartsAtTheMinimum()
    {
        IReadOnlyList<string> urls = WalkSites.CarsCom.BuildSearchUrls(
            Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025, yearMin: 2026));

        Assert.Contains("year_min=2026&", urls[1]);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_NoHybridOnlyRule_YieldsExactlyOneSearch()
    {
        IReadOnlyList<string> urls = WalkSites.CarsCom.BuildSearchUrls(Query("Toyota", "Corolla Hybrid", zip: "32833"));

        Assert.Equal(
            ["https://www.cars.com/shopping/results/?stock_type=used&makes[]=toyota&models[]=toyota-corolla_hybrid&zip=32833&maximum_distance=50&year_min=2019&mileage_max=100000"],
            urls);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_ShippedScenarioCamryHybrid_SplitsAtTheHybridOnlyYear()
    {
        IReadOnlyList<string> urls = WalkSites.CarsCom.BuildSearchUrls(ListingQuery.For(Shipped(), "Toyota Camry Hybrid"));

        Assert.Equal(2, urls.Count);
        Assert.Contains("models[]=toyota-camry_hybrid&", urls[0]);
        Assert.Contains("year_min=2018&", urls[0]);
        Assert.Contains("models[]=toyota-camry&", urls[1]);
        Assert.Contains("year_min=2025&", urls[1]);
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
    public void Carvana_BuildSearchUrls_NonHybridModel_HasNoFuelTypesFilter()
    {
        string url = WalkSites.Carvana.BuildSearchUrls(Query("Honda", "Insight")).Single();

        JsonElement filters = DecodeCvnaid(url);
        JsonElement makes = filters.GetProperty("filters").GetProperty("makes");
        Assert.Equal("Honda", makes[0].GetProperty("name").GetString());
        Assert.Equal("Insight", makes[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
        Assert.False(filters.GetProperty("filters").TryGetProperty("fuelTypes", out _));
        Assert.StartsWith("https://www.carvana.com/cars/filters?zip=32114&cvnaid=", url);
    }

    [Fact]
    public void Carvana_BuildSearchUrls_HybridModel_FiltersByBaseModelAndFuelType()
    {
        string url = WalkSites.Carvana.BuildSearchUrls(Query("Toyota", "Corolla Hybrid")).Single();

        JsonElement filters = DecodeCvnaid(url);
        JsonElement makes = filters.GetProperty("filters").GetProperty("makes");
        Assert.Equal("Toyota", makes[0].GetProperty("name").GetString());
        Assert.Equal("Corolla", makes[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
        Assert.Equal("Hybrid", filters.GetProperty("filters").GetProperty("fuelTypes")[0].GetString());
    }

    [Fact]
    public void Carvana_BuildSearchUrls_Prius_MatchesTheOrchestratorsVerifiedUrl()
    {
        // The whole URL here, route and zip included, is the one the orchestrator loaded over CDP
        // in Brian's Edge and confirmed carvana honored (notes/run-session-2026-09-22.md, "Year
        // and mileage facet URLs, verified by the orchestrator"): the chips read "2019 or newer"
        // and "Under 100,000 miles". The key is the singular "year"; a "years" key is ignored.
        string url = WalkSites.Carvana.BuildSearchUrls(Query("Toyota", "Prius")).Single();

        Assert.Equal(
            "https://www.carvana.com/cars/filters?zip=32114&cvnaid=" +
            "eyJmaWx0ZXJzIjp7Im1ha2VzIjpbeyJuYW1lIjoiVG95b3RhIiwicGFyZW50TW9kZWxzIjpbeyJuYW1lIjoiUHJpdXMifV19XSwieWVhciI6eyJtaW4iOjIwMTl9LCJtaWxlYWdlIjp7Im1heCI6MTAwMDAwfX19",
            url);
    }

    [Fact]
    public void Carvana_BuildSearchUrls_CvnaidIsUnpaddedBase64UrlOfTheFilterJson()
    {
        string url = WalkSites.Carvana.BuildSearchUrls(Query("Toyota", "Corolla Hybrid")).Single();

        string cvnaid = url[(url.IndexOf("cvnaid=", StringComparison.Ordinal) + "cvnaid=".Length)..];
        Assert.DoesNotContain('=', cvnaid);
        Assert.DoesNotContain('+', cvnaid);
        Assert.DoesNotContain('/', cvnaid);
        Assert.Equal(
            """{"filters":{"makes":[{"name":"Toyota","parentModels":[{"name":"Corolla"}]}],"fuelTypes":["Hybrid"],"year":{"min":2019},"mileage":{"max":100000}}}""",
            DecodeCvnaid(url).ToString());
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_ReadsYearAndMileageFromTheScenario()
    {
        Scenario scenario = Shipped();

        string prius = WalkSites.CarsCom.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Prius")).Single();
        IReadOnlyList<string> camry = WalkSites.CarsCom.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Camry Hybrid"));

        Assert.EndsWith("&year_min=2019&mileage_max=100000", prius);
        Assert.EndsWith("&year_min=2018&mileage_max=100000", camry[0]);
        Assert.EndsWith("&year_min=2025&mileage_max=100000", camry[1]);
    }

    [Fact]
    public void CarsCom_BuildSearchUrls_FollowsAChangedScenario()
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

        string prius = WalkSites.CarsCom.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Prius")).Single();
        IReadOnlyList<string> camry = WalkSites.CarsCom.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Camry Hybrid"));

        Assert.EndsWith("&year_min=2021&mileage_max=65000", prius);
        Assert.EndsWith("&year_min=2020&mileage_max=65000", camry[0]);
        Assert.EndsWith("&year_min=2025&mileage_max=65000", camry[1]);
    }

    [Fact]
    public void Carvana_BuildSearchUrls_ReadsYearAndMileageFromTheScenario_IncludingTheHybridOnlyCamry()
    {
        Scenario scenario = Shipped();

        JsonElement prius = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Prius")).Single()).GetProperty("filters");
        JsonElement camry = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Camry Hybrid")).Single()).GetProperty("filters");

        Assert.Equal(2019, prius.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(100000, prius.GetProperty("mileage").GetProperty("max").GetInt32());
        Assert.Equal(2018, camry.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(100000, camry.GetProperty("mileage").GetProperty("max").GetInt32());
        Assert.Equal("Hybrid", camry.GetProperty("fuelTypes")[0].GetString());
        Assert.Equal("Camry", camry.GetProperty("makes")[0].GetProperty("parentModels")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Carvana_BuildSearchUrls_FollowsAChangedScenario()
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

        JsonElement filters = DecodeCvnaid(WalkSites.Carvana.BuildSearchUrls(ListingQuery.For(scenario, "Toyota Prius")).Single()).GetProperty("filters");

        Assert.Equal(2021, filters.GetProperty("year").GetProperty("min").GetInt32());
        Assert.Equal(65000, filters.GetProperty("mileage").GetProperty("max").GetInt32());
    }

    [Fact]
    public void Carvana_BuildSearchUrls_HybridOnlyFromModelYear_StillFiltersByFuelType()
    {
        // Unlike cars.com, carvana's fuelTypes filter matches each listing's actual fuel type
        // rather than its title text, so a hybrid-only-from-year model needs no base-model
        // fallback: it already returns those listings under the normal hybrid query.
        string withFlag = WalkSites.Carvana.BuildSearchUrls(Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025)).Single();
        string withoutFlag = WalkSites.Carvana.BuildSearchUrls(Query("Toyota", "Camry Hybrid")).Single();

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
    [InlineData("autotrader")]
    [InlineData("CARS.COM")]
    public void Find_KnownSite_ReturnsIt(string name)
    {
        Assert.NotNull(WalkSites.Find(name));
    }

    [Fact]
    public void Find_UnknownSite_ReturnsNull()
    {
        Assert.Null(WalkSites.Find("edmunds"));
    }
}
