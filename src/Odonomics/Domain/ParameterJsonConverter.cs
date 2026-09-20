using System.Text.Json;
using System.Text.Json.Serialization;

namespace Odonomics.Domain;

/// <summary>
/// Reads a scenario parameter as either a plain number ("apr": 0.07, pinned) or an object with
/// min/max ("apr": {"min": 0.065, "max": 0.095}, loose) so scenarios/daughter.json stays readable
/// by hand. Writes the same shape back.
/// </summary>
public sealed class ParameterJsonConverter : JsonConverter<Parameter>
{
    public override Parameter Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Number)
        {
            return Parameter.Pinned(reader.GetDecimal());
        }

        if (reader.TokenType is not JsonTokenType.StartObject)
        {
            throw new JsonException("expected a number (pinned) or an object with min/max (loose)");
        }

        decimal? min = null;
        decimal? max = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string propertyName = reader.GetString() ?? throw new JsonException("expected a property name");
            reader.Read();
            switch (propertyName)
            {
                case "min":
                    min = reader.GetDecimal();
                    break;
                case "max":
                    max = reader.GetDecimal();
                    break;
                default:
                    throw new JsonException($"unknown loose-parameter field \"{propertyName}\"; expected \"min\" and \"max\"");
            }
        }

        if (min is null || max is null)
        {
            throw new JsonException("a loose parameter needs both \"min\" and \"max\"");
        }

        return Parameter.Loose(min.Value, max.Value);
    }

    public override void Write(Utf8JsonWriter writer, Parameter value, JsonSerializerOptions options)
    {
        if (value is LooseParameter loose)
        {
            writer.WriteStartObject();
            writer.WriteNumber("min", loose.Min);
            writer.WriteNumber("max", loose.Max);
            writer.WriteEndObject();
            return;
        }

        writer.WriteNumberValue(value.Expected);
    }
}
