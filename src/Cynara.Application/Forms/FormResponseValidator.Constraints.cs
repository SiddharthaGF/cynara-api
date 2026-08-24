using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cynara.Application.Forms;

public sealed partial class FormResponseValidator
{
    private static string ResolveRulesSchema(string clinicalSchemaJson, string? rulesSchemaJson)
    {
        if (!string.IsNullOrWhiteSpace(rulesSchemaJson))
        {
            return rulesSchemaJson;
        }

        JsonObject clinicalRoot = JsonParsing.ParseObject(clinicalSchemaJson, "clinical schema");
        string clinicalVersion = clinicalRoot["schemaVersion"]?.GetValue<string>() ?? "1.0.0";
        return $$"""
            {
              "schemaVersion": "1.0.0",
              "clinicalSchemaVersion": "{{clinicalVersion}}",
              "fields": {}
            }
            """;
    }

    private static Dictionary<string, object?> FlattenForRules(
        Dictionary<string, JsonElement> answers,
        Dictionary<string, AnswerFieldDefinition> fieldsByCode)
    {
        var flat = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach ((string code, JsonElement value) in answers)
        {
            if (fieldsByCode.TryGetValue(code, out AnswerFieldDefinition? field)
                && string.Equals(
                    field.Type,
                    FieldTypeNames.Repeater,
                    StringComparison.Ordinal))
            {
                FlattenRepeaterAnswer(flat, code, value);
                continue;
            }

            flat[code] = JsonElementToObject(value);
        }

        return flat;
    }

    private static void FlattenRepeaterAnswer(
        Dictionary<string, object?> flat,
        string code,
        JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            flat[code] = 0;
            return;
        }

        flat[code] = value.GetArrayLength();
        for (int rowIndex = 0; rowIndex < value.GetArrayLength(); rowIndex++)
        {
            JsonElement row = value[rowIndex];
            if (row.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty property in row.EnumerateObject())
            {
                flat[property.Name] = JsonElementToObject(property.Value);
            }
        }
    }

    private static bool ValidateConstraints(
        AnswerFieldDefinition field,
        object value,
        out FormResponseFieldError? error)
    {
        error = null;

        return field.Type switch
        {
            FieldTypeNames.Text or FieldTypeNames.Textarea =>
                ValidateStringConstraints(field, (string)value, out error),
            FieldTypeNames.Number =>
                ValidateNumericConstraints(
                    field,
                    Convert.ToDouble(value, CultureInfo.InvariantCulture),
                    out error),
            FieldTypeNames.Integer =>
                ValidateNumericConstraints(
                    field,
                    Convert.ToInt64(value, CultureInfo.InvariantCulture),
                    out error),
            _ => true,
        };
    }

    private static bool ValidateStringConstraints(
        AnswerFieldDefinition field,
        string value,
        out FormResponseFieldError? error)
    {
        error = null;
        if (field.Schema["minLength"]?.GetValue<int>() is int minLength && value.Length < minLength)
        {
            error = ConstraintError(field, string.Create(CultureInfo.InvariantCulture, $"must be at least {minLength} characters."));
            return false;
        }

        if (field.Schema["maxLength"]?.GetValue<int>() is int maxLength && value.Length > maxLength)
        {
            error = ConstraintError(field, string.Create(CultureInfo.InvariantCulture, $"must be at most {maxLength} characters."));
            return false;
        }

        if (field.Schema["pattern"]?.GetValue<string>() is { Length: > 0 } pattern)
        {
            try
            {
                if (!Regex.IsMatch(
                        value,
                        pattern,
                        RegexOptions.None,
                        TimeSpan.FromMilliseconds(1000)))
                {
                    error = ConstraintError(
                        field,
                        "does not match the required pattern.");
                    return false;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                error = ConstraintError(
                    field,
                    "does not match the required pattern.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateNumericConstraints(
        AnswerFieldDefinition field,
        double value,
        out FormResponseFieldError? error)
    {
        error = null;
        if (field.Schema["minimum"]?.GetValue<double>() is double minimum && value < minimum)
        {
            error = ConstraintError(field, string.Create(CultureInfo.InvariantCulture, $"must be greater than or equal to {minimum}."));
            return false;
        }

        if (field.Schema["maximum"]?.GetValue<double>() is double maximum && value > maximum)
        {
            error = ConstraintError(field, string.Create(CultureInfo.InvariantCulture, $"must be less than or equal to {maximum}."));
            return false;
        }

        if (field.Schema["multipleOf"]?.GetValue<double>() is double multipleOf
            && multipleOf != 0
            && Math.Abs((value / multipleOf) - Math.Round(value / multipleOf, MidpointRounding.ToEven)) > 0.000001)
        {
            error = ConstraintError(field, string.Create(CultureInfo.InvariantCulture, $"must be a multiple of {multipleOf}."));
            return false;
        }

        return true;
    }

    private static FormResponseFieldError ConstraintError(AnswerFieldDefinition field, string message)
    {
        return new("CONSTRAINT_VIOLATION", field.Path, $"Field '{field.Code}' {message}");
    }

    private static bool IsEmpty(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => true,
            JsonValueKind.String => value.GetString()?.Length == 0,
            JsonValueKind.Array => value.GetArrayLength() == 0,
            JsonValueKind.Object => !value.EnumerateObject().Any(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => false,
            _ => false,
        };
    }

    private static bool IsEmpty(object? value)
    {
        return value switch
        {
            null => true,
            string text => text.Length == 0,
            IEnumerable<object?> items => !items.Any(),
            _ => false,
        };
    }

    private static bool ValuesEqual(object left, object? right)
    {
        if (left is double leftNumber && right is double rightNumber)
        {
            return Math.Abs(leftNumber - rightNumber) < 0.000001;
        }

        if (left is long leftInteger && right is long rightInteger)
        {
            return leftInteger == rightInteger;
        }

        return left is IList<string> leftChoices && right is IList<string> rightChoices
            ? leftChoices.SequenceEqual(rightChoices, StringComparer.Ordinal)
            : Equals(left, right);
    }

    private static object? JsonElementToObject(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out long integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => throw new NotSupportedException(
                "Undefined JSON values are not supported."),
            JsonValueKind.Object => throw new NotSupportedException(
                "Nested JSON objects are not supported as answer values."),
            JsonValueKind.Array => throw new NotSupportedException(
                "Nested JSON arrays are not supported as answer values."),
            _ => value.GetRawText(),
        };
    }

    private static Dictionary<string, JsonElement> ParseAnswers(string answersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(answersJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ValidationException("Answers must be a JSON object.");
            }

            var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                answers[property.Name] = property.Value.Clone();
            }

            return answers;
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"Answers must be valid JSON: {exception.Message}");
        }
    }

    private FormRuleEvaluationResult EvaluateRules(
        string clinicalSchemaJson,
        string? rulesSchemaJson,
        string? uiSchemaJson,
        Dictionary<string, object?> ruleValues)
    {
        string resolvedRules = ResolveRulesSchema(clinicalSchemaJson, rulesSchemaJson);
        return ruleEngine.Evaluate(clinicalSchemaJson, resolvedRules, ruleValues, uiSchemaJson);
    }

    private sealed record RepeaterRowContext(
        FormRuleEvaluationResult Rules,
        FormResponseValidationMode Mode,
        List<FormResponseFieldError> Errors,
        List<Dictionary<string, object?>> Rows);

    private sealed record AnswerFieldDefinition(
        string Id,
        string Code,
        string Type,
        string Path,
        bool Required,
        bool ReadOnly,
        JsonObject Schema,
        IReadOnlyList<AnswerFieldDefinition> Children);

    private static class AnswerFieldIndex
    {
        public static Dictionary<string, AnswerFieldDefinition> Build(JsonObject clinicalRoot)
        {
            var byCode = new Dictionary<string, AnswerFieldDefinition>(StringComparer.Ordinal);
            if (clinicalRoot["fields"] is JsonArray fields)
            {
                IndexFields(fields, "/fields", byCode);
            }

            return byCode;
        }

        private static void IndexFields(
            JsonArray fields,
            string path,
            Dictionary<string, AnswerFieldDefinition> byCode)
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

                IReadOnlyList<AnswerFieldDefinition> children = [];
                if (field["items"] is JsonArray items)
                {
                    var childDefinitions = new List<AnswerFieldDefinition>();
                    IndexChildFields(items, $"{fieldPath}/items", childDefinitions);
                    children = childDefinitions;
                }

                var definition = new AnswerFieldDefinition(
                    id,
                    code,
                    type,
                    fieldPath,
                    field["required"]?.GetValue<bool>() ?? false,
                    field["readOnly"]?.GetValue<bool>() ?? false,
                    field,
                    children);

                if (type is not FieldTypeNames.Group)
                {
                    byCode[code] = definition;
                }

                if (field["items"] is JsonArray nestedItems && string.Equals(type, FieldTypeNames.Group, StringComparison.Ordinal))
                {
                    IndexFields(nestedItems, $"{fieldPath}/items", byCode);
                }
            }
        }

        private static void IndexChildFields(
            JsonArray items,
            string path,
            List<AnswerFieldDefinition> children)
        {
            for (int index = 0; index < items.Count; index++)
            {
                JsonObject field = items[index]!.AsObject();
                string fieldPath = string.Create(CultureInfo.InvariantCulture, $"{path}/{index}");
                string id = field["id"]?.GetValue<string>()
                    ?? throw new ValidationException($"Expected field id at {fieldPath}/id.");
                string code = field["code"]?.GetValue<string>()
                    ?? throw new ValidationException($"Expected field code at {fieldPath}/code.");
                string type = field["type"]?.GetValue<string>()
                    ?? throw new ValidationException($"Expected field type at {fieldPath}/type.");

                children.Add(new AnswerFieldDefinition(
                    id,
                    code,
                    type,
                    fieldPath,
                    field["required"]?.GetValue<bool>() ?? false,
                    field["readOnly"]?.GetValue<bool>() ?? false,
                    field,
                    []));
            }
        }
    }
}
