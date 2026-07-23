using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cynara.Application.Common;

namespace Cynara.Application.Modules.FormAi;

internal static partial class FormAiSanitizer
{
    private static readonly HashSet<string> AllowedWidgets =
    [
        WidgetNames.TextInput, WidgetNames.Textarea, WidgetNames.NumberInput,
        WidgetNames.IntegerInput, "checkbox",
        WidgetNames.Toggle, WidgetNames.DatePicker, WidgetNames.DateTimePicker,
        WidgetNames.TimePicker, WidgetNames.Select,
        "multi-select", "radio-group", "checkbox-group", WidgetNames.Group,
        WidgetNames.Repeater,
        "component", "hidden",
    ];

    private static readonly HashSet<string> AllowedWidths =
        ["full", "half", "third", "quarter"];

    private static readonly HashSet<string> AllowedRuleOperators =
    [
        "eq", "neq", "gt", "gte", "lt", "lte",
        "and", "or", "not",
        "empty", "coalesce",
        "add", "sub", "mul", "div",
    ];

    private static readonly Dictionary<string, string> TypeAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [FieldTypeNames.Text] = FieldTypeNames.Text,
            ["string"] = FieldTypeNames.Text,
            ["email"] = FieldTypeNames.Text,
            ["phone"] = FieldTypeNames.Text,
            ["tel"] = FieldTypeNames.Text,
            ["short"] = FieldTypeNames.Text,
            ["short-answer"] = FieldTypeNames.Text,
            ["short_text"] = FieldTypeNames.Text,
            [FieldTypeNames.Textarea] = FieldTypeNames.Textarea,
            ["paragraph"] = FieldTypeNames.Textarea,
            ["longtext"] = FieldTypeNames.Textarea,
            ["long-text"] = FieldTypeNames.Textarea,
            ["note"] = FieldTypeNames.Textarea,
            ["notes"] = FieldTypeNames.Textarea,
            [FieldTypeNames.Number] = FieldTypeNames.Number,
            ["float"] = FieldTypeNames.Number,
            ["decimal"] = FieldTypeNames.Number,
            ["double"] = FieldTypeNames.Number,
            [FieldTypeNames.Integer] = FieldTypeNames.Integer,
            ["int"] = FieldTypeNames.Integer,
            ["whole"] = FieldTypeNames.Integer,
            [FieldTypeNames.Boolean] = FieldTypeNames.Boolean,
            ["bool"] = FieldTypeNames.Boolean,
            ["checkbox"] = FieldTypeNames.Boolean,
            ["toggle"] = FieldTypeNames.Boolean,
            ["yes-no"] = FieldTypeNames.Boolean,
            [FieldTypeNames.Date] = FieldTypeNames.Date,
            [FieldTypeNames.DateTime] = FieldTypeNames.DateTime,
            ["date-time"] = FieldTypeNames.DateTime,
            ["timestamp"] = FieldTypeNames.DateTime,
            [FieldTypeNames.Time] = FieldTypeNames.Time,
            [FieldTypeNames.Choice] = FieldTypeNames.Choice,
            ["select"] = FieldTypeNames.Choice,
            ["radio"] = FieldTypeNames.Choice,
            ["dropdown"] = FieldTypeNames.Choice,
            ["enum"] = FieldTypeNames.Choice,
            ["options"] = FieldTypeNames.Choice,
            [FieldTypeNames.Group] = FieldTypeNames.Group,
            ["section"] = FieldTypeNames.Group,
            ["object"] = FieldTypeNames.Group,
            [FieldTypeNames.Repeater] = FieldTypeNames.Repeater,
            ["list"] = FieldTypeNames.Repeater,
            ["array"] = FieldTypeNames.Repeater,
            [FieldTypeNames.ComponentRef] = FieldTypeNames.ComponentRef,
            ["component"] = FieldTypeNames.ComponentRef,
        };

    private static readonly Dictionary<string, string> DefaultWidgets =
        new(StringComparer.Ordinal)
        {
            [FieldTypeNames.Text] = WidgetNames.TextInput,
            [FieldTypeNames.Textarea] = WidgetNames.Textarea,
            [FieldTypeNames.Number] = WidgetNames.NumberInput,
            [FieldTypeNames.Integer] = WidgetNames.IntegerInput,
            [FieldTypeNames.Boolean] = "checkbox",
            [FieldTypeNames.Date] = WidgetNames.DatePicker,
            [FieldTypeNames.DateTime] = WidgetNames.DateTimePicker,
            [FieldTypeNames.Time] = WidgetNames.TimePicker,
            [FieldTypeNames.Choice] = WidgetNames.Select,
            [FieldTypeNames.Group] = WidgetNames.Group,
            [FieldTypeNames.Repeater] = WidgetNames.Repeater,
            [FieldTypeNames.ComponentRef] = "component",
        };

    private static readonly string[] PresentationKeys =
    [
        SchemaJsonKeys.Label, "helpText", "placeholder", SchemaJsonKeys.Widget,
        SchemaJsonKeys.Width, "hidden", "order",
        "timePresets", "accessibility",
    ];

    private static readonly string[] FieldRuleKeys =
    [
        "visibleWhen", "enabledWhen", "requiredWhen", SchemaJsonKeys.Calculate,
    ];

    [GeneratedRegex("-+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HyphenRegex { get; }

    [GeneratedRegex(
        "[^A-Z0-9]+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex InvalidValidationCharactersRegex { get; }

    [GeneratedRegex(
        @"^\d+\.\d+\.\d+",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex SemverPrefixRegex { get; }

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
        string schemaVersion =
            AsSemver(source[SchemaJsonKeys.SchemaVersion]?.GetValue<string>())
            ?? "1.0.0";
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        var fields = new JsonArray();
        if (source[SchemaJsonKeys.Fields] is JsonArray inputFields)
        {
            int index = 0;
            foreach (JsonNode? item in inputFields)
            {
                JsonObject? field = SanitizeField(
                    item,
                    stolenUi,
                    usedIds,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"field-{++index}"));
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
                [SchemaJsonKeys.Id] = "field-1",
                [SchemaJsonKeys.Code] = "field.1",
                [SchemaJsonKeys.Type] = FieldTypeNames.Text,
            });
            stolenUi["field-1"] = new JsonObject
            {
                [SchemaJsonKeys.Label] = "Field 1",
                [SchemaJsonKeys.Widget] = WidgetNames.TextInput,
            };
        }

        return new JsonObject
        {
            [SchemaJsonKeys.SchemaVersion] = schemaVersion,
            [SchemaJsonKeys.Fields] = fields,
        };
    }

    private static JsonObject? SanitizeField(
        JsonNode? raw,
        Dictionary<string, JsonObject> stolenUi,
        HashSet<string> usedIds,
        string fallbackId)
    {
        if (raw is not JsonObject source
            || !TryResolveFieldType(source, out string type))
        {
            return null;
        }

        string id = UniqueId(
            SlugifyKebab(
                source[SchemaJsonKeys.Id]?.GetValue<string>() ?? fallbackId),
            usedIds);
        _ = usedIds.Add(id);
        string code = SlugifyCode(
            source[SchemaJsonKeys.Code]?.GetValue<string>() ?? id);
        var result = new JsonObject
        {
            [SchemaJsonKeys.Id] = id,
            [SchemaJsonKeys.Code] =
                string.IsNullOrWhiteSpace(code) ? $"field.{id}" : code,
            [SchemaJsonKeys.Type] = type,
        };

        if (!TryBuildNormalizedField(
                source,
                type,
                id,
                stolenUi,
                result))
        {
            return null;
        }

        stolenUi[id] = StealPresentation(source, type, id);
        return result;
    }

    private static bool TryResolveFieldType(JsonObject source, out string type)
    {
        string? rawType = source[SchemaJsonKeys.Type]?.GetValue<string>();
        if (rawType is null
            || !TypeAliases.TryGetValue(rawType.Trim(), out string? resolved))
        {
            type = string.Empty;
            return false;
        }

        type = resolved;
        return true;
    }

    private static bool TryBuildNormalizedField(
        JsonObject source,
        string type,
        string id,
        Dictionary<string, JsonObject> stolenUi,
        JsonObject result)
    {
        CopyCommonClinical(source, result);
        switch (type)
        {
            case FieldTypeNames.Text:
                Copy(source, result, "minLength", "maxLength", "pattern");
                break;
            case FieldTypeNames.Textarea:
                Copy(source, result, "minLength", "maxLength");
                break;
            case FieldTypeNames.Number:
                Copy(
                    source,
                    result,
                    "minimum",
                    "maximum",
                    "multipleOf",
                    "decimalPlaces");
                break;
            case FieldTypeNames.Integer:
            case FieldTypeNames.Date:
            case FieldTypeNames.DateTime:
            case FieldTypeNames.Time:
                Copy(source, result, "minimum", "maximum");
                break;
            case FieldTypeNames.Choice:
                result[SchemaJsonKeys.Options] =
                    SanitizeOptions(source[SchemaJsonKeys.Options] as JsonArray);
                Copy(source, result, "allowMultiple");
                break;
            case FieldTypeNames.Boolean:
                break;
            case FieldTypeNames.Group:
            case FieldTypeNames.Repeater:
                result[SchemaJsonKeys.Items] = SanitizeChildren(
                    source[SchemaJsonKeys.Items] as JsonArray,
                    stolenUi,
                    id,
                    type);
                if (string.Equals(
                        type,
                        FieldTypeNames.Repeater,
                        StringComparison.Ordinal))
                {
                    result["repeatable"] = true;
                    Copy(source, result, "minItems", "maxItems");
                }

                break;
            case FieldTypeNames.ComponentRef:
                if (source["componentCode"]?.GetValue<string>()
                        is not string componentCode
                    || string.IsNullOrWhiteSpace(componentCode))
                {
                    return false;
                }

                result["componentCode"] = componentCode.Trim();
                Copy(source, result, "componentVersion");
                break;
            default:
                throw new ArgumentException(
                    $"Unknown field type: {type}",
                    nameof(source));
        }

        return true;
    }

    private static JsonObject StealPresentation(
        JsonObject source,
        string type,
        string id)
    {
        var presentation = new JsonObject();
        foreach (string key in PresentationKeys)
        {
            if (source[key] is JsonNode value)
            {
                presentation[key] = value.DeepClone();
            }
        }

        if (source["title"]?.GetValue<string>() is string title
            && !string.IsNullOrWhiteSpace(title)
            && presentation[SchemaJsonKeys.Label] is null)
        {
            presentation[SchemaJsonKeys.Label] = title.Trim();
        }

        string widget =
            presentation[SchemaJsonKeys.Widget]?.GetValue<string>()
            ?? string.Empty;
        presentation[SchemaJsonKeys.Widget] = AllowedWidgets.Contains(widget)
            ? widget
            : DefaultWidgets[type];
        if (presentation[SchemaJsonKeys.Width]?.GetValue<string>() is string width
            && !AllowedWidths.Contains(width))
        {
            _ = presentation.Remove(SchemaJsonKeys.Width);
        }

        if (presentation[SchemaJsonKeys.Label] is not JsonValue label
            || string.IsNullOrWhiteSpace(label.GetValue<string>()))
        {
            presentation[SchemaJsonKeys.Label] = Humanize(id);
        }

        return presentation;
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
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{parentId}-item-{++index}"));
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
                [SchemaJsonKeys.Id] = $"{parentId}-item",
                [SchemaJsonKeys.Code] = $"{parentId}.item",
                [SchemaJsonKeys.Type] = FieldTypeNames.Text,
            });
            stolenUi[$"{parentId}-item"] = new JsonObject
            {
                [SchemaJsonKeys.Label] = string.Equals(
                    type,
                    FieldTypeNames.Repeater,
                    StringComparison.Ordinal)
                    ? "Item"
                    : "Value",
                [SchemaJsonKeys.Widget] = WidgetNames.TextInput,
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
        return children.Count == 0
            ? null
            : new JsonObject
            {
                [SchemaJsonKeys.Type] = "section",
                ["title"] = title[..Math.Min(title.Length, 256)],
                [SchemaJsonKeys.Children] = children,
            };
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
        }

        return result;
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
            if (field[SchemaJsonKeys.Items] is JsonArray children)
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

    private static void Copy(
        JsonObject source,
        JsonObject result,
        params string[] keys)
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
                    ?? string.Create(
                        CultureInfo.InvariantCulture,
                        $"option-{++index}");
                string label =
                    option[SchemaJsonKeys.Label]?.GetValue<string>()?.Trim()
                    ?? optionValue;
                options.Add(new JsonObject
                {
                    ["value"] =
                        optionValue[..Math.Min(optionValue.Length, 128)],
                    [SchemaJsonKeys.Label] =
                        label[..Math.Min(label.Length, 256)],
                });
            }
        }

        if (options.Count == 0)
        {
            options.Add(new JsonObject
            {
                ["value"] = "option-1",
                [SchemaJsonKeys.Label] = "Option 1",
            });
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
            string candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{id}-{suffix}");
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
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            _ = builder.Append(
                char.IsLetterOrDigit(character)
                    ? char.ToLowerInvariant(character)
                    : '-');
        }

        string result = HyphenRegex.Replace(builder.ToString(), "-").Trim('-');
        return result.Length > 0 && char.IsLetter(result[0])
            ? result[..Math.Min(result.Length, 64)]
            : "field";
    }

    private static string SlugifyCode(string value)
    {
        string result = SlugifyKebab(value).Replace('-', '.');
        return result[..Math.Min(result.Length, 128)];
    }

    private static string ToValidationCode(string? raw, string fallback)
    {
        string source = string.IsNullOrWhiteSpace(raw) ? fallback : raw;
        string code = InvalidValidationCharactersRegex
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
        if (value is null || !SemverPrefixRegex.IsMatch(value))
        {
            return null;
        }

        return value.Split(['-', '+'], count: 2)[0];
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
}

internal sealed record SanitizedAiTriple(
    JsonObject Clinical,
    JsonObject Ui,
    JsonObject Rules);
