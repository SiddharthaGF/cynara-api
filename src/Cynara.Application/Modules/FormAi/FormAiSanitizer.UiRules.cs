using System.Globalization;
using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

internal static partial class FormAiSanitizer
{
    private static JsonObject SanitizeUi(
        JsonNode? raw,
        JsonObject clinical,
        Dictionary<string, JsonObject> stolenUi)
    {
        JsonObject source = AsObject(raw);
        string version =
            AsSemver(clinical[SchemaJsonKeys.SchemaVersion]?.GetValue<string>())
            ?? "1.0.0";
        var fields = new JsonObject();
        foreach (JsonObject clinicalField in EnumerateFields(
                     clinical[SchemaJsonKeys.Fields] as JsonArray))
        {
            string id = clinicalField[SchemaJsonKeys.Id]!.GetValue<string>();
            JsonObject fromModel =
                source[SchemaJsonKeys.Fields]?[id] as JsonObject ?? [];
            JsonObject fromClinical = stolenUi.GetValueOrDefault(id) ?? [];
            var presentation = new JsonObject();
            foreach (string key in PresentationKeys)
            {
                JsonNode? value = fromModel[key] ?? fromClinical[key];
                if (value is not null)
                {
                    presentation[key] = value.DeepClone();
                }
            }

            string type = clinicalField[SchemaJsonKeys.Type]!.GetValue<string>();
            if (presentation[SchemaJsonKeys.Widget]?.GetValue<string>()
                    is not string widget
                || !AllowedWidgets.Contains(widget))
            {
                presentation[SchemaJsonKeys.Widget] = DefaultWidgets[type];
            }

            if (presentation[SchemaJsonKeys.Label] is not JsonValue label
                || string.IsNullOrWhiteSpace(label.GetValue<string>()))
            {
                presentation[SchemaJsonKeys.Label] = Humanize(id);
            }

            fields[id] = presentation;
        }

        return new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] =
                AsSemver(source[SchemaJsonKeys.SchemaVersion]?.GetValue<string>())
                ?? version,
            [SchemaJsonKeys.ClinicalSchemaVersion] = version,
            [SchemaJsonKeys.Fields] = fields,
            [SchemaJsonKeys.Layout] =
                SanitizeLayout(source[SchemaJsonKeys.Layout], fields),
        };
    }

    private static JsonObject SanitizeRules(JsonNode? raw, JsonObject clinical)
    {
        JsonObject source = AsObject(raw);
        CollectKnownFieldKeys(
            clinical,
            out HashSet<string> knownIds,
            out HashSet<string> knownCodes);

        var fields = new JsonObject();
        if (source[SchemaJsonKeys.Fields] is JsonObject fieldRules)
        {
            foreach ((string id, JsonNode? value) in fieldRules)
            {
                JsonObject? next = SanitizeFieldRuleObject(
                    id,
                    value,
                    knownIds,
                    knownCodes);
                if (next is not null)
                {
                    fields[id] = next;
                }
            }
        }

        var validations = new JsonArray();
        var usedCodes = new HashSet<string>(StringComparer.Ordinal);
        if (source[SchemaJsonKeys.Validations] is JsonArray inputValidations)
        {
            int index = 0;
            foreach (JsonNode? value in inputValidations)
            {
                JsonObject? clean = SanitizeValidationEntry(
                    value,
                    knownCodes,
                    usedCodes,
                    ref index);
                if (clean is not null)
                {
                    validations.Add(clean);
                }
            }
        }

        string version =
            AsSemver(clinical[SchemaJsonKeys.SchemaVersion]?.GetValue<string>())
            ?? "1.0.0";
        return new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] =
                AsSemver(source[SchemaJsonKeys.SchemaVersion]?.GetValue<string>())
                ?? version,
            [SchemaJsonKeys.ClinicalSchemaVersion] = version,
            [SchemaJsonKeys.Fields] = fields,
            [SchemaJsonKeys.Validations] = validations,
        };
    }

    private static void CollectKnownFieldKeys(
        JsonObject clinical,
        out HashSet<string> knownIds,
        out HashSet<string> knownCodes)
    {
        knownIds = new HashSet<string>(StringComparer.Ordinal);
        knownCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject field in EnumerateFields(
                     clinical[SchemaJsonKeys.Fields] as JsonArray))
        {
            _ = knownIds.Add(field[SchemaJsonKeys.Id]!.GetValue<string>());
            _ = knownCodes.Add(field[SchemaJsonKeys.Code]!.GetValue<string>());
        }
    }

    private static JsonObject? SanitizeFieldRuleObject(
        string id,
        JsonNode? value,
        HashSet<string> knownIds,
        HashSet<string> knownCodes)
    {
        if (!knownIds.Contains(id) || value is not JsonObject rules)
        {
            return null;
        }

        var next = new JsonObject();
        foreach (string key in FieldRuleKeys)
        {
            if (rules[key] is JsonNode expression
                && ExpressionReferencesKnown(expression, knownCodes))
            {
                next[key] = expression.DeepClone();
            }
        }

        return next.Count > 0 ? next : null;
    }

    private static JsonObject? SanitizeValidationEntry(
        JsonNode? value,
        HashSet<string> knownCodes,
        HashSet<string> usedCodes,
        ref int index)
    {
        if (value is not JsonObject validation
            || validation["assert"] is not JsonNode assert
            || validation[SchemaJsonKeys.Message]?.GetValue<string>()
                is not string message
            || !ExpressionReferencesKnown(assert, knownCodes))
        {
            return null;
        }

        if (validation["when"] is JsonNode when
            && !ExpressionReferencesKnown(when, knownCodes))
        {
            return null;
        }

        string code = ToValidationCode(
            validation[SchemaJsonKeys.Code]?.GetValue<string>(),
            string.Create(
                CultureInfo.InvariantCulture,
                $"VALIDATION_{++index}"));
        while (!usedCodes.Add(code))
        {
            code = ToValidationCode(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{code}_{index + 1}"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"VALIDATION_{++index}"));
        }

        var clean = new JsonObject
        {
            [SchemaJsonKeys.Code] = code,
            [SchemaJsonKeys.Message] = message[..Math.Min(message.Length, 500)],
            ["assert"] = assert.DeepClone(),
        };
        if (validation["when"] is JsonNode validWhen)
        {
            clean["when"] = validWhen.DeepClone();
        }

        return clean;
    }

    private static JsonArray SanitizeLayout(JsonNode? raw, JsonObject fields)
    {
        var result = new JsonArray();
        if (raw is not JsonArray input)
        {
            return result;
        }

        foreach (JsonNode? node in input)
        {
            JsonObject? clean = SanitizeLayoutNode(node, fields);
            if (clean is not null)
            {
                result.Add(clean);
            }
        }

        return result;
    }

    private static JsonObject? SanitizeLayoutNode(
        JsonNode? raw,
        JsonObject fields)
    {
        if (raw is not JsonObject source
            || source[SchemaJsonKeys.Type]?.GetValue<string>() is not string type)
        {
            return null;
        }

        if (string.Equals(type, "field", StringComparison.Ordinal))
        {
            return TrySanitizeFieldLayout(source, fields);
        }

        if (string.Equals(type, "section", StringComparison.Ordinal))
        {
            return TrySanitizeSectionLayout(source, fields);
        }

        if (type is FieldTypeNames.Group or FieldTypeNames.Repeater)
        {
            return TrySanitizeGroupOrRepeaterLayout(source, type, fields);
        }

        return null;
    }

    private static JsonObject? TrySanitizeFieldLayout(
        JsonObject source,
        JsonObject fields)
    {
        string? fieldId = source[SchemaJsonKeys.FieldId]?.GetValue<string>();
        return fieldId is not null && fields.ContainsKey(fieldId)
            ? new JsonObject
            {
                [SchemaJsonKeys.Type] = "field",
                [SchemaJsonKeys.FieldId] = fieldId,
            }
            : null;
    }

    private static JsonObject? TrySanitizeSectionLayout(
        JsonObject source,
        JsonObject fields)
    {
        JsonArray children =
            SanitizeLayout(source[SchemaJsonKeys.Children], fields);
        string title =
            source["title"]?.GetValue<string>()?.Trim() ?? "Section";
        if (children.Count == 0)
        {
            return null;
        }

        var result = new JsonObject
        {
            [SchemaJsonKeys.Type] = "section",
            ["title"] = title[..Math.Min(title.Length, 256)],
            [SchemaJsonKeys.Children] = children,
        };
        CopyLayoutText(source, result, "description", 1000);
        return result;
    }

    private static JsonObject? TrySanitizeGroupOrRepeaterLayout(
        JsonObject source,
        string type,
        JsonObject fields)
    {
        string? fieldId = source[SchemaJsonKeys.FieldId]?.GetValue<string>();
        JsonNode? childrenNode = string.Equals(
                type,
                FieldTypeNames.Group,
                StringComparison.Ordinal)
            ? source[SchemaJsonKeys.Children]
            : source["itemTemplate"];
        JsonArray children = SanitizeLayout(childrenNode, fields);
        if (fieldId is null
            || !fields.ContainsKey(fieldId)
            || children.Count == 0)
        {
            return null;
        }

        var result = new JsonObject
        {
            [SchemaJsonKeys.Type] = type,
            [SchemaJsonKeys.FieldId] = fieldId,
        };
        if (string.Equals(type, FieldTypeNames.Group, StringComparison.Ordinal))
        {
            result[SchemaJsonKeys.Children] = children;
        }
        else
        {
            result["itemTemplate"] = children;
            CopyLayoutText(source, result, "addButtonLabel", 128);
            CopyLayoutText(source, result, "removeButtonLabel", 128);
        }

        return result;
    }

    private static void CopyLayoutText(
        JsonObject source,
        JsonObject result,
        string key,
        int maxLength)
    {
        if (source[key]?.GetValue<string>() is string value)
        {
            result[key] = value[..Math.Min(value.Length, maxLength)];
        }
    }

    private static bool ExpressionReferencesKnown(
        JsonNode expression,
        HashSet<string> knownCodes)
    {
        if (expression is JsonObject obj
            && obj["ref"]?.GetValue<string>() is string reference)
        {
            return knownCodes.Contains(reference);
        }

        if (expression is JsonObject expressionObject
            && expressionObject["args"] is JsonArray args)
        {
            if (expressionObject["op"]?.GetValue<string>() is string op
                && !AllowedRuleOperators.Contains(op))
            {
                return false;
            }

            return args.All(
                item => item is JsonNode node
                    && ExpressionReferencesKnown(node, knownCodes));
        }

        return expression is JsonObject objWithoutRef
            && objWithoutRef.ContainsKey("lit");
    }
}
