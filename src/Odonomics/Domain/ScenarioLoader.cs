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
        Scenario? scenario = JsonSerializer.Deserialize<Scenario>(json, Options);
        return scenario ?? throw new JsonException("scenario file deserialized to null");
    }
}
