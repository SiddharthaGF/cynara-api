using System.Globalization;
using System.Text.Json.Nodes;

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
}
