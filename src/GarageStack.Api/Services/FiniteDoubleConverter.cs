using System.Text.Json;
using System.Text.Json.Serialization;

namespace GarageStack.Api.Services;

public sealed class FiniteDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        return reader.GetDouble();
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value == null || !double.IsFinite(value.Value))
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}
