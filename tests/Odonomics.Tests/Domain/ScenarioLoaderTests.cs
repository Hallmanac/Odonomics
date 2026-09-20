using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class ScenarioLoaderTests
{
    private static string DaughterScenarioPath => Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json");

    [Fact]
    public void Load_ShippedDaughterScenario_LoadsAndValidates()
    {
        Scenario scenario = ScenarioLoader.Load(DaughterScenarioPath);

        Assert.Equal("daughter", scenario.Name);
        Assert.Equal("32114", scenario.Zip);
        Assert.Equal(10, scenario.HoldYears);
        Assert.Equal(120, scenario.HoldMonths);
        Assert.Equal(60, scenario.TermMonths);
        Assert.True(scenario.Apr.IsLoose);
        Assert.Equal(0.065m, scenario.Apr.Low);
        Assert.Equal(0.095m, scenario.Apr.High);
        Assert.False(scenario.DownPayment.IsLoose);
        Assert.Equal(3000m, scenario.DownPayment.Expected);
        Assert.Equal(2019, scenario.Filters.MinYearFor("Honda Insight"));
        Assert.Equal(2018, scenario.Filters.MinYearFor("Toyota Camry Hybrid"));
        Assert.Null(scenario.InsuranceMonthlyByModel["Honda Insight"]);
        Assert.Equal(95m, scenario.InsuranceMonthlyByModel["Toyota Corolla Hybrid"]);
        Assert.Contains(300m, scenario.TargetMonthlyBudgets);
    }

    [Fact]
    public void Parse_PinnedAndLooseParametersRoundTrip()
    {
        const string json = """
            {
              "name": "test",
              "zip": "32114",
              "radiusMiles": 50,
              "annualMiles": 12000,
              "gasPricePerGallon": { "min": 3.00, "max": 3.60 },
              "holdYears": 10,
              "downPayment": 3000,
              "apr": { "min": 0.065, "max": 0.095 },
              "termMonths": 60,
              "maintenancePerMile": 0.07,
              "emergencyReservePerMonth": 50,
              "salesTaxStateRate": 0.06,
              "countySurtaxRate": 0.005,
              "countySurtaxSource": "test",
              "fees": 500,
              "residualFraction": 0.35,
              "insuranceMonthlyByModel": {},
              "mpgByModel": {},
              "filters": {
                "minModelYear": 2019,
                "minModelYearOverrides": {},
                "maxMileage": 100000,
                "allowedModels": []
              },
              "targetMonthlyBudgets": [300]
            }
            """;

        Scenario scenario = ScenarioLoader.Parse(json);

        Assert.False(scenario.AnnualMiles.IsLoose);
        Assert.Equal(12000m, scenario.AnnualMiles.Expected);
        Assert.True(scenario.GasPricePerGallon.IsLoose);
        Assert.Equal(3.00m, scenario.GasPricePerGallon.Low);
        Assert.Equal(3.60m, scenario.GasPricePerGallon.High);
    }
}
