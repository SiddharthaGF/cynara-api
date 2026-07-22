using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cynara.Application.Modules.FormAi;

internal static partial class FormAiSanitizer
{
    private static readonly HashSet<string> AllowedWidgets =
    [
        "text-input", "textarea", "number-input", "integer-input", "checkbox",
        "toggle", "date-picker", "datetime-picker", "time-picker", "select",
        "multi-select", "radio-group", "checkbox-group", "group", "repeater",
        "component", "hidden",
    ];

    private static readonly HashSet<string> AllowedWidths = ["full", "half", "third", "quarter"];

    private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = "text",
        ["string"] = "text",
        ["email"] = "text",
        ["phone"] = "text",
        ["tel"] = "text",
        ["short"] = "text",
        ["short-answer"] = "text",
        ["short_text"] = "text",
        ["textarea"] = "textarea",
        ["paragraph"] = "textarea",
        ["longtext"] = "textarea",
        ["long-text"] = "textarea",
        ["note"] = "textarea",
        ["notes"] = "textarea",
        ["number"] = "number",
        ["float"] = "number",
        ["decimal"] = "number",
        ["double"] = "number",
        ["integer"] = "integer",
        ["int"] = "integer",
        ["whole"] = "integer",
        ["boolean"] = "boolean",
        ["bool"] = "boolean",
        ["checkbox"] = "boolean",
        ["toggle"] = "boolean",
        ["yes-no"] = "boolean",
        ["date"] = "date",
        ["datetime"] = "datetime",
        ["date-time"] = "datetime",
        ["timestamp"] = "datetime",
        ["time"] = "time",
        ["choice"] = "choice",
        ["select"] = "choice",
        ["radio"] = "choice",
        ["dropdown"] = "choice",
        ["enum"] = "choice",
        ["options"] = "choice",
        ["group"] = "group",
        ["section"] = "group",
        ["object"] = "group",
        ["repeater"] = "repeater",
        ["list"] = "repeater",
        ["array"] = "repeater",
        ["component-ref"] = "component-ref",
        ["component"] = "component-ref",
    };

    private static readonly Dictionary<string, string> DefaultWidgets = new(StringComparer.Ordinal)
    {
        ["text"] = "text-input",
        ["textarea"] = "textarea",
        ["number"] = "number-input",
        ["integer"] = "integer-input",
        ["boolean"] = "checkbox",
        ["date"] = "date-picker",
        ["datetime"] = "datetime-picker",
        ["time"] = "time-picker",
        ["choice"] = "select",
        ["group"] = "group",
        ["repeater"] = "repeater",
        ["component-ref"] = "component",
    };

    public static SanitizedAiTriple Sanitize(
        JsonNode? clinicalNode,
        JsonNode? uiNode,
        JsonNode? rulesNode)
    {
        var stolenUi = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        JsonObject clinical = SanitizeClinical(clinicalNode, stolenUi);
        JsonObject ui = SanitizeUi(uiNode, clinical, stolenUi);
        JsonObject rules = SanitizeRules(rulesNode, clinical);
        return new SanitizedAiTriple(clinical, ui, rules);
    }

    private static JsonObject SanitizeClinical(
        JsonNode? raw,
        Dictionary<string, JsonObject> stolenUi)
    {
        JsonObject source = AsObject(raw);
        string schemaVersion = AsSemver(source["schemaVersion"]?.GetValue<string>()) ?? "1.0.0";
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var fields = new JsonArray();
        if (source["fields"] is JsonArray inputFields)
        {
            int index = 0;
            foreach (JsonNode? item in inputFields)
            {
                JsonObject? field = SanitizeField(
                    item,
                    stolenUi,
                    usedIds,
                    $"field-{++index}");
                if (field is not null)
                {
                    fields.Add(field);
                }
            }
        }

        if (fields.Count == 0)
        {
            fields.Add(new JsonObject
            {
                ["id"] = "field-1",
                ["code"] = "field.1",
                ["type"] = "text",
            });
            stolenUi["field-1"] = new JsonObject
            {
                ["label"] = "Field 1",
                ["widget"] = "text-input",
            };
        }

        return new JsonObject
        {
            ["schemaVersion"] = schemaVersion,
            ["fields"] = fields,
        };
    }

    private static JsonObject? SanitizeField(
        JsonNode? raw,
        Dictionary<string, JsonObject> stolenUi,
        HashSet<string> usedIds,
        string fallbackId)
    {
        if (raw is not JsonObject source)
        {
            return null;
        }

        string? rawType = source["type"]?.GetValue<string>();
        if (rawType is null || !TypeAliases.TryGetValue(rawType.Trim(), out string? type))
        {
            return null;
        }

        string id = UniqueId(SlugifyKebab(
            source["id"]?.GetValue<string>() ?? fallbackId), usedIds);
        _ = usedIds.Add(id);
        string code = SlugifyCode(source["code"]?.GetValue<string>() ?? id);
        var result = new JsonObject
        {
            ["id"] = id,
            ["code"] = string.IsNullOrWhiteSpace(code) ? $"field.{id}" : code,
            ["type"] = type,
        };

        CopyCommonClinical(source, result);
        switch (type)
        {
            case "text":
                Copy(source, result, "minLength", "maxLength", "pattern");
                break;
            case "textarea":
                Copy(source, result, "minLength", "maxLength");
                break;
            case "number":
                Copy(source, result, "minimum", "maximum", "multipleOf", "decimalPlaces");
                break;
            case "integer":
            case "date":
            case "datetime":
            case "time":
                Copy(source, result, "minimum", "maximum");
                break;
            case "choice":
                result["options"] = SanitizeOptions(source["options"] as JsonArray);
                Copy(source, result, "allowMultiple");
                break;
            case "boolean":
                break;
            case "group":
            case "repeater":
                result["items"] = SanitizeChildren(
                    source["items"] as JsonArray,
                    stolenUi,
                    id,
                    type);
                if (type == "repeater")
                {
                    result["repeatable"] = true;
                    Copy(source, result, "minItems", "maxItems");
                }
                break;
            case "component-ref":
                if (source["componentCode"]?.GetValue<string>() is not string componentCode
                    || string.IsNullOrWhiteSpace(componentCode))
                {
                    return null;
                }

                result["componentCode"] = componentCode.Trim();
                Copy(source, result, "componentVersion");
                break;
            default:
                throw new ArgumentException($"Unknown field type: {type}");
        }

        var presentation = new JsonObject();
        foreach (string key in new[]
        {
            "label", "helpText", "placeholder", "widget", "width", "hidden", "order",
            "timePresets", "accessibility",
        })
        {
            if (source[key] is JsonNode value)
            {
                presentation[key] = value.DeepClone();
            }
        }

        if (source["title"]?.GetValue<string>() is string title
            && !string.IsNullOrWhiteSpace(title)
            && presentation["label"] is null)
        {
            presentation["label"] = title.Trim();
        }

        string widget = presentation["widget"]?.GetValue<string>() ?? "";
        presentation["widget"] = AllowedWidgets.Contains(widget)
            ? widget
            : DefaultWidgets[type];
        if (presentation["width"]?.GetValue<string>() is string width
            && !AllowedWidths.Contains(width))
        {
            _ = presentation.Remove("width");
        }

        if (presentation["label"] is not JsonValue label
            || string.IsNullOrWhiteSpace(label.GetValue<string>()))
        {
            presentation["label"] = Humanize(id);
        }

        stolenUi[id] = presentation;
        return result;
    }

    private static JsonArray SanitizeChildren(
        JsonArray? input,
        Dictionary<string, JsonObject> stolenUi,
        string parentId,
        string type)
    {
        var children = new JsonArray();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        if (input is not null)
        {
            foreach (JsonNode? item in input)
            {
                JsonObject? field = SanitizeField(
                    item,
                    stolenUi,
                    usedIds,
                    $"{parentId}-item-{++index}");
                if (field is not null)
                {
                    children.Add(field);
                }
            }
        }

        if (children.Count == 0)
        {
            children.Add(new JsonObject
            {
                ["id"] = $"{parentId}-item",
                ["code"] = $"{parentId}.item",
                ["type"] = "text",
            });
            stolenUi[$"{parentId}-item"] = new JsonObject
            {
                ["label"] = type == "repeater" ? "Item" : "Value",
                ["widget"] = "text-input",
            };
        }

        return children;
    }

    private static JsonObject SanitizeUi(
        JsonNode? raw,
        JsonObject clinical,
        Dictionary<string, JsonObject> stolenUi)
    {
        JsonObject source = AsObject(raw);
        string version = AsSemver(clinical["schemaVersion"]?.GetValue<string>()) ?? "1.0.0";
        var fields = new JsonObject();
        foreach (JsonObject clinicalField in EnumerateFields(clinical["fields"] as JsonArray))
        {
            string id = clinicalField["id"]!.GetValue<string>();
            JsonObject fromModel = source["fields"]?[id] as JsonObject ?? [];
            JsonObject fromClinical = stolenUi.GetValueOrDefault(id) ?? [];
            var presentation = new JsonObject();
            foreach (string key in new[]
            {
                "label", "helpText", "placeholder", "widget", "width", "hidden", "order",
                "timePresets", "accessibility",
            })
            {
                JsonNode? value = fromModel[key] ?? fromClinical[key];
                if (value is not null)
                {
                    presentation[key] = value.DeepClone();
                }
            }

            string type = clinicalField["type"]!.GetValue<string>();
            if (presentation["widget"]?.GetValue<string>() is not string widget
                || !AllowedWidgets.Contains(widget))
            {
                presentation["widget"] = DefaultWidgets[type];
            }

            if (presentation["label"] is not JsonValue label
                || string.IsNullOrWhiteSpace(label.GetValue<string>()))
            {
                presentation["label"] = Humanize(id);
            }
            fields[id] = presentation;
        }

        return new JsonObject
        {
            ["schemaVersion"] = AsSemver(source["schemaVersion"]?.GetValue<string>()) ?? version,
            ["clinicalSchemaVersion"] = version,
            ["fields"] = fields,
            ["layout"] = SanitizeLayout(source["layout"], fields),
        };
    }

    private static JsonObject SanitizeRules(JsonNode? raw, JsonObject clinical)
    {
        JsonObject source = AsObject(raw);
        var knownIds = new HashSet<string>(StringComparer.Ordinal);
        var knownCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonObject field in EnumerateFields(clinical["fields"] as JsonArray))
        {
            _ = knownIds.Add(field["id"]!.GetValue<string>());
            _ = knownCodes.Add(field["code"]!.GetValue<string>());
        }

        var fields = new JsonObject();
        if (source["fields"] is JsonObject fieldRules)
        {
            foreach ((string id, JsonNode? value) in fieldRules)
            {
                if (!knownIds.Contains(id) || value is not JsonObject rules)
                {
                    continue;
                }

                var next = new JsonObject();
                foreach (string key in new[] { "visibleWhen", "enabledWhen", "requiredWhen", "calculate" })
                {
                    if (rules[key] is JsonNode expression
                        && ExpressionReferencesKnown(expression, knownCodes))
                    {
                        next[key] = expression.DeepClone();
                    }
                }
                if (next.Count > 0)
                {
                    fields[id] = next;
                }
            }
        }

        var validations = new JsonArray();
        var usedCodes = new HashSet<string>(StringComparer.Ordinal);
        if (source["validations"] is JsonArray inputValidations)
        {
            int index = 0;
            foreach (JsonNode? value in inputValidations)
            {
                if (value is not JsonObject validation
                    || validation["assert"] is not JsonNode assert
                    || validation["message"]?.GetValue<string>() is not string message
                    || !ExpressionReferencesKnown(assert, knownCodes))
                {
                    continue;
                }

                if (validation["when"] is JsonNode when
                    && !ExpressionReferencesKnown(when, knownCodes))
                {
                    continue;
                }

                string code = ToValidationCode(
                    validation["code"]?.GetValue<string>(),
                    $"VALIDATION_{++index}");
                while (!usedCodes.Add(code))
                {
                    code = ToValidationCode($"{code}_{index + 1}", $"VALIDATION_{++index}");
                }

                var clean = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message[..Math.Min(message.Length, 500)],
                    ["assert"] = assert.DeepClone(),
                };
                if (validation["when"] is JsonNode validWhen)
                {
                    clean["when"] = validWhen.DeepClone();
                }
                validations.Add(clean);
            }
        }

        string version = AsSemver(clinical["schemaVersion"]?.GetValue<string>()) ?? "1.0.0";
        return new JsonObject
        {
            ["schemaVersion"] = AsSemver(source["schemaVersion"]?.GetValue<string>()) ?? version,
            ["clinicalSchemaVersion"] = version,
            ["fields"] = fields,
            ["validations"] = validations,
        };
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

    private static JsonObject? SanitizeLayoutNode(JsonNode? raw, JsonObject fields)
    {
        if (raw is not JsonObject source || source["type"]?.GetValue<string>() is not string type)
        {
            return null;
        }

        if (type == "field")
        {
            string? fieldId = source["fieldId"]?.GetValue<string>();
            return fieldId is not null && fields.ContainsKey(fieldId)
                ? new JsonObject { ["type"] = "field", ["fieldId"] = fieldId }
                : null;
        }

        if (type == "section")
        {
            JsonArray children = SanitizeLayout(source["children"], fields);
            string title = source["title"]?.GetValue<string>()?.Trim() ?? "Section";
            return children.Count == 0
                ? null
                : new JsonObject
                {
                    ["type"] = "section",
                    ["title"] = title[..Math.Min(title.Length, 256)],
                    ["children"] = children,
                };
        }

        if (type is "group" or "repeater")
        {
            string? fieldId = source["fieldId"]?.GetValue<string>();
            JsonNode? childrenNode = type == "group"
                ? source["children"]
                : source["itemTemplate"];
            JsonArray children = SanitizeLayout(childrenNode, fields);
            return fieldId is null || !fields.ContainsKey(fieldId) || children.Count == 0
                ? null
                : new JsonObject
                {
                    ["type"] = type,
                    ["fieldId"] = fieldId,
                    [type == "group" ? "children" : "itemTemplate"] = children,
                };
        }

        return null;
    }

    private static bool ExpressionReferencesKnown(JsonNode expression, HashSet<string> knownCodes)
    {
        if (expression is JsonObject obj
            && obj["ref"]?.GetValue<string>() is string reference)
        {
            return knownCodes.Contains(reference);
        }
        else if (expression is JsonObject expressionObject
                                && expressionObject["args"] is JsonArray args)
        {
            return args.All(item => item is JsonNode node && ExpressionReferencesKnown(node, knownCodes));
        }
        else
        {
            return expression is JsonObject objWithoutRef
                                && objWithoutRef.ContainsKey("lit");
        }
    }

    private static IEnumerable<JsonObject> EnumerateFields(JsonArray? fields)
    {
        if (fields is null)
        {
            yield break;
        }

        foreach (JsonNode? value in fields)
        {
            if (value is not JsonObject field)
            {
                continue;
            }

            yield return field;
            if (field["items"] is JsonArray children)
            {
                foreach (JsonObject child in EnumerateFields(children))
                {
                    yield return child;
                }
            }
        }
    }

    private static void CopyCommonClinical(JsonObject source, JsonObject result)
    {
        Copy(source, result, "required", "readOnly", "description", "default");
    }

    private static void Copy(JsonObject source, JsonObject result, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (source[key] is JsonNode value)
            {
                result[key] = value.DeepClone();
            }
        }
    }

    private static JsonArray SanitizeOptions(JsonArray? input)
    {
        var options = new JsonArray();
        if (input is not null)
        {
            int index = 0;
            foreach (JsonNode? value in input)
            {
                if (value is not JsonObject option)
                {
                    continue;
                }

                string optionValue = option["value"]?.GetValue<string>()?.Trim()
                    ?? $"option-{++index}";
                string label = option["label"]?.GetValue<string>()?.Trim() ?? optionValue;
                options.Add(new JsonObject
                {
                    ["value"] = optionValue[..Math.Min(optionValue.Length, 128)],
                    ["label"] = label[..Math.Min(label.Length, 256)],
                });
            }
        }

        if (options.Count == 0)
        {
            options.Add(new JsonObject { ["value"] = "option-1", ["label"] = "Option 1" });
        }
        return options;
    }

    private static string UniqueId(string id, HashSet<string> used)
    {
        if (!used.Contains(id))
        {
            return id;
        }

        for (int suffix = 2; suffix < 1000; suffix++)
        {
            string candidate = $"{id}-{suffix}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
        return $"{id}-x";
    }

    private static string SlugifyKebab(string value)
    {
        string normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (char character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            _ = builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        string result = HyphenRegex().Replace(builder.ToString(), "-").Trim('-');
        return result.Length > 0 && char.IsLetter(result[0]) ? result[..Math.Min(result.Length, 64)] : "field";
    }

    private static string SlugifyCode(string value)
    {
        string result = SlugifyKebab(value).Replace('-', '.');
        return result[..Math.Min(result.Length, 128)];
    }

    private static string ToValidationCode(string? raw, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
        string code = InvalidValidationCharactersRegex()
            .Replace(source.ToUpperInvariant(), "_")
            .Trim('_');
        if (code.Length == 0 || !char.IsLetter(code[0]))
        {
            code = $"V_{code}";
        }
        code = code[..Math.Min(code.Length, 64)];
        return code.Length >= 3 ? code : fallback;
    }

    private static string? AsSemver(string? value)
    {
        return value is not null && SemverPrefixRegex().IsMatch(value)
            ? value.Split(['-', '+'])[0]
            : null;
    }

    private static JsonObject AsObject(JsonNode? node)
    {
        return node is JsonObject value ? value : [];
    }

    private static string Humanize(string id)
    {
        string text = id.Replace('-', ' ').Trim();
        return text.Length == 0
            ? "Field"
            : char.ToUpperInvariant(text[0]) + text[1..];
    }

    [GeneratedRegex("-+")]
    private static partial Regex HyphenRegex();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex InvalidValidationCharactersRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+")]
    private static partial Regex SemverPrefixRegex();
}

internal sealed record SanitizedAiTriple(
    JsonObject Clinical,
    JsonObject Ui,
    JsonObject Rules);
