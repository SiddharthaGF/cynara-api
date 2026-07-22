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

    private static void WriteNode(Utf8JsonWriter writer, JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (string propertyName in obj.Select(static pair => pair.Key).OrderBy(static key => key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(propertyName);
                    WriteNode(writer, obj[propertyName]!);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (JsonNode? item in array)
                {
                    WriteNode(writer, item!);
                }

                writer.WriteEndArray();
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
