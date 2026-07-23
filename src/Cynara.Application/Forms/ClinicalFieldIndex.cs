using System.Globalization;
using System.Text.Json.Nodes;

namespace Cynara.Application.Forms;

internal static class ClinicalFieldIndex
{
    public static Dictionary<string, FieldInfo> BuildById(JsonObject clinicalRoot)
    {
        var byId = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        if (clinicalRoot["fields"] is JsonArray fields)
        {
            IndexFields(fields, "/fields", byId);
        }

        return byId;
    }

    public static Dictionary<string, FieldInfo> BuildByCode(JsonObject clinicalRoot)
    {
        return BuildById(clinicalRoot).Values.ToDictionary(static item => item.Code, static item => item, StringComparer.Ordinal);
    }

    private static void IndexFields(JsonArray fields, string path, Dictionary<string, FieldInfo> byId)
    {
        for (int index = 0; index < fields.Count; index++)
        {
            JsonObject field = fields[index]!.AsObject();
            string fieldPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{index}");
            string id = field["id"]?.GetValue<string>()
                ?? throw new ValidationException($"Expected field id at {fieldPath}/id.");
            string code = field["code"]?.GetValue<string>()
                ?? throw new ValidationException($"Expected field code at {fieldPath}/code.");
            string type = field["type"]?.GetValue<string>()
                ?? throw new ValidationException($"Expected field type at {fieldPath}/type.");

            byId[id] = new FieldInfo(
                id,
                code,
                type,
                field["required"]?.GetValue<bool>() ?? false,
                field["readOnly"]?.GetValue<bool>() ?? false,
                fieldPath,
                field["multipleOf"]?.GetValue<double>(),
                field["decimalPlaces"]?.GetValue<int>());

            if (field["items"] is JsonArray items)
            {
                IndexFields(items, $"{fieldPath}/items", byId);
            }
        }
    }

    internal sealed record FieldInfo(
        string Id,
        string Code,
        string Type,
        bool Required,
        bool ReadOnly,
        string Path,
        double? MultipleOf,
        int? DecimalPlaces);
}
