using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Common;

internal static class CanonicalJsonSerializer
{
    public static string Serialize(JsonNode node)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteNode(writer, node);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                WriteObject(writer, obj);
                break;
            case JsonArray array:
                WriteArray(writer, array);
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonObject obj)
    {
        writer.WriteStartObject();
        foreach (KeyValuePair<string, JsonNode?> property in obj.OrderBy(
            static pair => pair.Key,
            StringComparer.Ordinal))
        {
            writer.WritePropertyName(property.Key);
            WriteNode(writer, property.Value);
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(Utf8JsonWriter writer, JsonArray array)
    {
        writer.WriteStartArray();
        foreach (JsonNode? item in array)
        {
            WriteNode(writer, item);
        }

        writer.WriteEndArray();
    }
}
