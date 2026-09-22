using System.Text.Json;

namespace Odonomics.Domain;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static Scenario Load(string path)
    {
        string json = File.ReadAllText(path);
        return Parse(json);
    }

    public static Scenario Parse(string json)
    {
        Scenario scenario = JsonSerializer.Deserialize<Scenario>(json, Options)
            ?? throw new JsonException("scenario file deserialized to null");

        ValidateHybridOnlyFromModelYear(scenario);
        return scenario;
    }

    private static void ValidateHybridOnlyFromModelYear(Scenario scenario)
    {
        foreach ((string makeModel, int year) in scenario.HybridOnlyFromModelYear)
        {
            if (!scenario.Filters.AllowedModels.Contains(makeModel, StringComparer.OrdinalIgnoreCase))
            {
                throw new JsonException($"hybridOnlyFromModelYear key \"{makeModel}\" is not one of the scenario's allowedModels");
            }

            if (year is < 1000 or > 9999)
            {
                throw new JsonException($"hybridOnlyFromModelYear[\"{makeModel}\"] = {year} is not a four-digit year");
            }
        }
    }
}
