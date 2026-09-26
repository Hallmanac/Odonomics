using System.Text.Json;
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
        Assert.Equal("32833", scenario.Zip);
        Assert.Equal(50, scenario.RadiusMiles);
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
        Assert.Equal(2025, scenario.HybridOnlyFromModelYear["Toyota Camry Hybrid"]);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearOmitted_DefaultsToEmpty()
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

        Assert.Empty(scenario.HybridOnlyFromModelYear);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearKeyDifferentCase_MatchesCaseInsensitively()
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
                "allowedModels": ["Toyota Camry Hybrid"]
              },
              "hybridOnlyFromModelYear": { "toyota camry hybrid": 2025 },
              "targetMonthlyBudgets": [300]
            }
            """;

        Scenario scenario = ScenarioLoader.Parse(json);

        Assert.Equal(2025, scenario.HybridOnlyFromModelYear["Toyota Camry Hybrid"]);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearExplicitNull_DefaultsToEmpty()
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
              "hybridOnlyFromModelYear": null,
              "targetMonthlyBudgets": [300]
            }
            """;

        Scenario scenario = ScenarioLoader.Parse(json);

        Assert.Empty(scenario.HybridOnlyFromModelYear);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearKeyNotAnAllowedModel_Throws()
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
                "allowedModels": ["Toyota Corolla Hybrid"]
              },
              "hybridOnlyFromModelYear": { "Toyota Camry Hybrid": 2025 },
              "targetMonthlyBudgets": [300]
            }
            """;

        JsonException ex = Assert.Throws<JsonException>(() => ScenarioLoader.Parse(json));
        Assert.Contains("Toyota Camry Hybrid", ex.Message);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearValueNotFourDigits_Throws()
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
                "allowedModels": ["Toyota Camry Hybrid"]
              },
              "hybridOnlyFromModelYear": { "Toyota Camry Hybrid": 25 },
              "targetMonthlyBudgets": [300]
            }
            """;

        JsonException ex = Assert.Throws<JsonException>(() => ScenarioLoader.Parse(json));
        Assert.Contains("four-digit year", ex.Message);
    }

    [Fact]
    public void Parse_HybridOnlyFromModelYearDuplicateKeyDifferentCase_Throws()
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
                "allowedModels": ["Toyota Camry Hybrid"]
              },
              "hybridOnlyFromModelYear": { "toyota camry hybrid": 2025, "Toyota Camry Hybrid": 2026 },
              "targetMonthlyBudgets": [300]
            }
            """;

        JsonException ex = Assert.Throws<JsonException>(() => ScenarioLoader.Parse(json));
        Assert.Contains("Toyota Camry Hybrid", ex.Message);
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

    private static string ScenarioJson(string extraFields) => $$"""
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
          {{extraFields}}
          "targetMonthlyBudgets": [300]
        }
        """;

    [Fact]
    public void Parse_FulfillmentOmitted_DefaultsToDelivery()
    {
        Assert.Equal(Fulfillment.Delivery, ScenarioLoader.Parse(ScenarioJson("")).Fulfillment);
    }

    [Theory]
    [InlineData("delivery", Fulfillment.Delivery)]
    [InlineData("pickup", Fulfillment.Pickup)]
    [InlineData("Pickup", Fulfillment.Pickup)]
    public void Parse_FulfillmentNamed_IsRead(string value, Fulfillment expected)
    {
        Assert.Equal(expected, ScenarioLoader.Parse(ScenarioJson($"\"fulfillment\": \"{value}\",")).Fulfillment);
    }

    [Theory]
    [InlineData("\"shipping\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Parse_FulfillmentNotAKnownValue_Throws(string value)
    {
        Assert.ThrowsAny<JsonException>(() => ScenarioLoader.Parse(ScenarioJson($"\"fulfillment\": {value},")));
    }

    [Fact]
    public void Load_ShippedDaughterScenario_AssumesDelivery()
    {
        Assert.Equal(Fulfillment.Delivery, ScenarioLoader.Load(DaughterScenarioPath).Fulfillment);
    }
}
