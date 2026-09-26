using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odonomics.Domain;

public static class ScenarioLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
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

        scenario = scenario with
        {
            HybridOnlyFromModelYear = BuildCaseInsensitiveHybridOnlyFromModelYear(
                scenario.HybridOnlyFromModelYear ?? new Dictionary<string, int>()),
        };

        ValidateHybridOnlyFromModelYear(scenario);
        return scenario;
    }

    private static Dictionary<string, int> BuildCaseInsensitiveHybridOnlyFromModelYear(
        IReadOnlyDictionary<string, int> hybridOnlyFromModelYear)
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string makeModel, int year) in hybridOnlyFromModelYear)
        {
            if (!result.TryAdd(makeModel, year))
            {
                throw new JsonException(
                    $"hybridOnlyFromModelYear has \"{makeModel}\" more than once (keys are matched case-insensitively)");
            }
        }

        return result;
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
