using System.Globalization;
using System.Text.Json.Nodes;

using Cynara.Application.Common;

namespace Cynara.Application.Forms;

public sealed class FormRuleEngine : IFormRuleEngine
{
    public FormRuleEvaluationResult Evaluate(
        string clinicalSchemaJson,
        string rulesSchemaJson,
        IReadOnlyDictionary<string, object?> values,
        string? uiSchemaJson = null)
    {
        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
        JsonObject rulesRoot = ParseObject(rulesSchemaJson, "rules schema");
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById =
            ClinicalFieldIndex.BuildById(clinicalRoot);
        RuleDependencyMetadata metadata = FormRuleAnalyzer.Analyze(
            clinicalSchemaJson,
            rulesSchemaJson);

        var workingValues = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        var visibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        var required = new Dictionary<string, bool>(StringComparer.Ordinal);
        var calculatedValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        SeedFieldDefaults(fieldsById, uiSchemaJson, visibility, enabled, required);

        if (rulesRoot[SchemaJsonKeys.Fields] is JsonObject fieldRules)
        {
            ApplyCalculations(
                fieldRules,
                metadata,
                fieldsById,
                workingValues,
                calculatedValues);
            ApplyFieldPredicates(
                fieldRules,
                fieldsById,
                workingValues,
                visibility,
                enabled,
                required);
        }

        List<RuleValidationError> validationErrors =
            CollectValidationErrors(rulesRoot, workingValues);

        return new FormRuleEvaluationResult(
            visibility,
            enabled,
            required,
            calculatedValues,
            validationErrors);
    }

    private static void SeedFieldDefaults(
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        string? uiSchemaJson,
        Dictionary<string, bool> visibility,
        Dictionary<string, bool> enabled,
        Dictionary<string, bool> required)
    {
        foreach (ClinicalFieldIndex.FieldInfo field in fieldsById.Values)
        {
            bool defaultHidden = IsUiHidden(uiSchemaJson, field.Id);
            visibility[field.Id] = !defaultHidden;
            enabled[field.Id] = !field.ReadOnly;
            required[field.Id] = field.Required;
        }
    }

    private static void ApplyCalculations(
        JsonObject fieldRules,
        RuleDependencyMetadata metadata,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        Dictionary<string, object?> workingValues,
        Dictionary<string, object?> calculatedValues)
    {
        foreach (string fieldId in metadata.EvaluationOrder)
        {
            if (!fieldRules.TryGetPropertyValue(fieldId, out JsonNode? rulesNode)
                || rulesNode is not JsonObject rules
                || rules[SchemaJsonKeys.Calculate] is not JsonNode calculateNode
                || !fieldsById.TryGetValue(
                    fieldId,
                    out ClinicalFieldIndex.FieldInfo? fieldInfo))
            {
                continue;
            }

            object? calculated = NumericPrecision.NormalizeCalculatedValue(
                EvaluateExpression(calculateNode, workingValues),
                fieldInfo);
            calculatedValues[fieldInfo.Code] = calculated;
            workingValues[fieldInfo.Code] = calculated;
        }
    }

    private static void ApplyFieldPredicates(
        JsonObject fieldRules,
        Dictionary<string, ClinicalFieldIndex.FieldInfo> fieldsById,
        Dictionary<string, object?> workingValues,
        Dictionary<string, bool> visibility,
        Dictionary<string, bool> enabled,
        Dictionary<string, bool> required)
    {
        foreach ((string fieldId, JsonNode? rulesNode) in fieldRules)
        {
            if (rulesNode is not JsonObject rules || !fieldsById.ContainsKey(fieldId))
            {
                continue;
            }

            if (rules["visibleWhen"] is JsonNode visibleWhen)
            {
                visibility[fieldId] = ToBoolean(
                    EvaluateExpression(visibleWhen, workingValues));
            }

            if (rules["enabledWhen"] is JsonNode enabledWhen)
            {
                enabled[fieldId] = ToBoolean(
                    EvaluateExpression(enabledWhen, workingValues));
            }

            if (rules["requiredWhen"] is JsonNode requiredWhen)
            {
                required[fieldId] = ToBoolean(
                    EvaluateExpression(requiredWhen, workingValues));
            }
        }
    }

    private static List<RuleValidationError> CollectValidationErrors(
        JsonObject rulesRoot,
        Dictionary<string, object?> workingValues)
    {
        var validationErrors = new List<RuleValidationError>();
        if (rulesRoot[SchemaJsonKeys.Validations] is not JsonArray validations)
        {
            return validationErrors;
        }

        for (int index = 0; index < validations.Count; index++)
        {
            JsonObject validation = validations[index]!.AsObject();
            string code = validation[SchemaJsonKeys.Code]?.GetValue<string>()
                ?? string.Create(CultureInfo.InvariantCulture, $"VALIDATION_{index}");
            string message = validation[SchemaJsonKeys.Message]?.GetValue<string>()
                ?? "Validation failed.";

            if (validation["when"] is JsonNode whenNode
                && !ToBoolean(EvaluateExpression(whenNode, workingValues)))
            {
                continue;
            }

            if (validation["assert"] is JsonNode assertNode
                && !ToBoolean(EvaluateExpression(assertNode, workingValues)))
            {
                validationErrors.Add(new RuleValidationError(code, message));
            }
        }

        return validationErrors;
    }

    private static object? EvaluateExpression(
        JsonNode node,
        IReadOnlyDictionary<string, object?> values)
    {
        if (node is JsonObject obj)
        {
            if (obj["ref"]?.GetValue<string>() is { Length: > 0 } fieldCode)
            {
                return values.TryGetValue(fieldCode, out object? value) ? value : null;
            }

            if (obj["lit"] is JsonNode literalNode)
            {
                return JsonNodeToObject(literalNode);
            }

            string op = obj["op"]?.GetValue<string>()
                ?? throw new ValidationException("Expression node is missing op.");
            JsonArray args = obj["args"] as JsonArray
                ?? throw new ValidationException($"Expression '{op}' is missing args.");

            return op switch
            {
                "eq" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) == 0),
                "neq" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) != 0),
                "gt" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) > 0),
                "gte" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) >= 0),
                "lt" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) < 0),
                "lte" => Compare(
                    args,
                    values,
                    static (left, right) => CompareValues(left, right) <= 0),
                "and" => args.All(item => ToBoolean(EvaluateExpression(item!, values))),
                "or" => args.Any(item => ToBoolean(EvaluateExpression(item!, values))),
                "not" => !ToBoolean(EvaluateExpression(args[0]!, values)),
                "empty" => IsEmpty(EvaluateExpression(args[0]!, values)),
                "coalesce" => Coalesce(args, values),
                "add" => Arithmetic(args, values, static (left, right) => left + right),
                "sub" => Arithmetic(args, values, static (left, right) => left - right),
                "mul" => Arithmetic(args, values, static (left, right) => left * right),
                "div" => Arithmetic(args, values, static (left, right) => left / right),
                _ => throw new ValidationException(
                    $"Unsupported expression operator '{op}'."),
            };
        }

        throw new ValidationException("Invalid expression node.");
    }

    private static object? Coalesce(
        JsonArray args,
        IReadOnlyDictionary<string, object?> values)
    {
        foreach (JsonNode? arg in args)
        {
            object? value = EvaluateExpression(arg!, values);
            if (!IsEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool Compare(
        JsonArray args,
        IReadOnlyDictionary<string, object?> values,
        Func<object?, object?, bool> predicate)
    {
        object? left = EvaluateExpression(args[0]!, values);
        object? right = EvaluateExpression(args[1]!, values);
        return predicate(left, right);
    }

    private static double? Arithmetic(
        JsonArray args,
        IReadOnlyDictionary<string, object?> values,
        Func<double, double, double> compute)
    {
        object? left = EvaluateExpression(args[0]!, values);
        object? right = EvaluateExpression(args[1]!, values);
        if (IsEmpty(left) || IsEmpty(right))
        {
            return null;
        }

        double result = compute(ToDouble(left), ToDouble(right));
        return double.IsFinite(result) ? result : null;
    }

    private static bool IsEmpty(object? value)
    {
        return value switch
        {
            null => true,
            double number => !double.IsFinite(number),
            float number => !float.IsFinite(number),
            string text => text.Length == 0,
            JsonArray array => array.Count == 0,
            IEnumerable<object?> items => !items.Any(),
            _ => false,
        };
    }

    private static bool ToBoolean(object? value)
    {
        return value switch
        {
            bool boolean => boolean,
            null => false,
            string text => text.Length > 0,
            double number => number != 0,
            int number => number != 0,
            long number => number != 0,
            decimal number => number != 0,
            JsonValue jsonValue => ToBoolean(JsonNodeToObject(jsonValue)),
            _ => true,
        };
    }

    private static int CompareValues(object? left, object? right)
    {
        if (left is null || right is null)
        {
            if (left is null && right is null)
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            return 1;
        }

        if (left is string leftText && right is string rightText)
        {
            return string.CompareOrdinal(leftText, rightText);
        }

        return ToDouble(left).CompareTo(ToDouble(right));
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            decimal number => (double)number,
            JsonValue jsonValue => Convert.ToDouble(
                JsonNodeToObject(jsonValue),
                CultureInfo.InvariantCulture),
            _ => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };
    }

    private static object? JsonNodeToObject(JsonNode node)
    {
        return node switch
        {
            JsonValue value when value.TryGetValue(out string? text) => text,
            JsonValue value when value.TryGetValue(out bool boolean) => boolean,
            JsonValue value when value.TryGetValue(out int integer) => integer,
            JsonValue value when value.TryGetValue(out long longInteger) => longInteger,
            JsonValue value when value.TryGetValue(out double number) => number,
            JsonValue value when value.TryGetValue(out decimal decimalNumber) =>
                decimalNumber,
            JsonArray array => array
                .Select(static item => item is null ? null : JsonNodeToObject(item))
                .ToList(),
            _ => node.ToJsonString(),
        };
    }

    private static bool IsUiHidden(string? uiSchemaJson, string fieldId)
    {
        if (uiSchemaJson is null)
        {
            return false;
        }

        JsonObject uiRoot = ParseObject(uiSchemaJson, "UI schema");
        return uiRoot[SchemaJsonKeys.Fields] is JsonObject fields
            && fields.TryGetPropertyValue(fieldId, out JsonNode? presentation)
            && presentation is JsonObject presentationObject
            && (presentationObject["hidden"]?.GetValue<bool>() ?? false);
    }

    private static JsonObject ParseObject(string json, string label)
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
