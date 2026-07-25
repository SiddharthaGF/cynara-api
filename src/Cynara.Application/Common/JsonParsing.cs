using System.Text.Json.Nodes;

namespace Cynara.Application.Common;

internal static class JsonParsing
{
    public static JsonObject ParseObject(string json, string label)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                ?? throw new ValidationException(
                    $"Invalid {label}: expected a JSON object.");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ValidationException($"Invalid {label}: {exception.Message}");
        }
    }
}
