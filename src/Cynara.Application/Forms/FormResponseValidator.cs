using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cynara.Application.Common;

namespace Cynara.Application.Forms;

public sealed class FormResponseValidator(IFormRuleEngine ruleEngine) : IFormResponseValidator
{
    public FormResponseValidationResult Validate(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        string answersJson,
        FormResponseValidationMode mode)
    {
        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
        Dictionary<string, AnswerFieldDefinition> fieldsByCode = AnswerFieldIndex.Build(clinicalRoot);
        var repeatersByCode = fieldsByCode.Values
            .Where(item => item.Type == "repeater")
            .ToDictionary(item => item.Code, item => item, StringComparer.Ordinal);

        Dictionary<string, JsonElement> answers = ParseAnswers(answersJson);
        var errors = new List<FormResponseFieldError>();

        ValidateKnownTopLevelKeys(answers, fieldsByCode, errors);

        Dictionary<string, object?> ruleValues = FlattenForRules(answers, fieldsByCode);
        FormRuleEvaluationResult rules = EvaluateRules(
            clinicalSchemaJson,
            rulesSchemaJson,
            uiSchemaJson,
            ruleValues);

        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (AnswerFieldDefinition field in fieldsByCode.Values)
        {
            if (field.Type is "group" or "repeater" or "component-ref")
            {
                continue;
            }

            _ = answers.TryGetValue(field.Code, out JsonElement value);
            ValidateScalarField(
                field,
                value,
                rules,
                mode,
                errors,
                normalized);
        }

        foreach ((string repeaterCode, AnswerFieldDefinition repeater) in repeatersByCode)
        {
            _ = answers.TryGetValue(repeaterCode, out JsonElement rawRows);
            ValidateRepeater(repeater, rawRows, rules, mode, errors, normalized);
        }

        ApplyCalculatedFields(answers, fieldsByCode, rules, normalized, mode, errors);

        if (mode == FormResponseValidationMode.Complete)
        {
            AppendCrossFieldRuleErrors(rules, errors);
        }

        string normalizedAnswersJson = JsonSerializer.Serialize(normalized, CanonicalJsonOptions.Instance);
        return new FormResponseValidationResult(normalizedAnswersJson, errors);
    }

    private static void ValidateKnownTopLevelKeys(
        Dictionary<string, JsonElement> answers,
        Dictionary<string, AnswerFieldDefinition> fieldsByCode,
        List<FormResponseFieldError> errors)
    {
        var allowed = fieldsByCode.Values
            .Where(item => item.Type is not ("group" or "component-ref"))
            .Select(item => item.Code)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in answers.Keys)
        {
            if (!allowed.Contains(key))
            {
                errors.Add(new FormResponseFieldError(
                    "UNKNOWN_FIELD",
                    $"/answers/{key}",
                    $"Unknown answer field '{key}'."));
            }
        }
    }

    private static void ValidateScalarField(
        AnswerFieldDefinition field,
        JsonElement value,
        FormRuleEvaluationResult rules,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors,
        Dictionary<string, object?> normalized)
    {
        bool visible = !rules.Visibility.TryGetValue(field.Id, out bool isVisible) || isVisible;
        bool enabled = !rules.Enabled.TryGetValue(field.Id, out bool isEnabled) || isEnabled;
        bool required = (rules.Required.TryGetValue(field.Id, out bool isRequired) && isRequired)
            || field.Required;

        if (rules.CalculatedValues.ContainsKey(field.Code))
        {
            return;
        }

        if (!visible || !enabled)
        {
            if (!IsEmpty(value))
            {
                errors.Add(new FormResponseFieldError(
                    visible ? "DISABLED_FIELD_VALUE" : "HIDDEN_FIELD_VALUE",
                    field.Path,
                    $"Field '{field.Code}' cannot accept values while hidden or disabled."));
            }

            return;
        }

        if (field.ReadOnly && !rules.CalculatedValues.ContainsKey(field.Code))
        {
            if (!IsEmpty(value))
            {
                errors.Add(new FormResponseFieldError(
                    "READONLY_FIELD_MODIFIED",
                    field.Path,
                    $"Field '{field.Code}' is read-only."));
            }

            return;
        }

        if (IsEmpty(value))
        {
            if (mode == FormResponseValidationMode.Complete && required)
            {
                errors.Add(new FormResponseFieldError(
                    "REQUIRED_FIELD_MISSING",
                    field.Path,
                    $"Field '{field.Code}' is required."));
            }

            return;
        }

        if (!TryConvertValue(field, value, out object? converted, out FormResponseFieldError? typeError))
        {
            errors.Add(typeError!);
            return;
        }

        if (!ValidateConstraints(field, converted!, out FormResponseFieldError? constraintError))
        {
            errors.Add(constraintError!);
            return;
        }

        normalized[field.Code] = converted;
    }

    private static void ValidateRepeater(
        AnswerFieldDefinition repeater,
        JsonElement rawRows,
        FormRuleEvaluationResult rules,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors,
        Dictionary<string, object?> normalized)
    {
        bool visible = !rules.Visibility.TryGetValue(repeater.Id, out bool isVisible) || isVisible;
        bool enabled = !rules.Enabled.TryGetValue(repeater.Id, out bool isEnabled) || isEnabled;

        if (!visible || !enabled)
        {
            if (rawRows.ValueKind == JsonValueKind.Array && rawRows.GetArrayLength() > 0)
            {
                errors.Add(new FormResponseFieldError(
                    visible ? "DISABLED_FIELD_VALUE" : "HIDDEN_FIELD_VALUE",
                    repeater.Path,
                    $"Repeater '{repeater.Code}' cannot accept values while hidden or disabled."));
            }

            normalized[repeater.Code] = Array.Empty<Dictionary<string, object?>>();
            return;
        }

        if (rawRows.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            rawRows = default;
        }

        if (rawRows.ValueKind != JsonValueKind.Array)
        {
            if (!IsEmpty(rawRows))
            {
                errors.Add(new FormResponseFieldError(
                    "INVALID_TYPE",
                    repeater.Path,
                    $"Repeater '{repeater.Code}' must be an array."));
            }

            normalized[repeater.Code] = Array.Empty<Dictionary<string, object?>>();
            ValidateRepeaterItemCount(repeater, 0, mode, errors);
            return;
        }

        int rowCount = rawRows.GetArrayLength();
        ValidateRepeaterItemCount(repeater, rowCount, mode, errors);

        List<Dictionary<string, object?>> rows = [];
        var childCodes = repeater.Children
            .Select(item => item.Code)
            .ToHashSet(StringComparer.Ordinal);

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            JsonElement rowElement = rawRows[rowIndex];
            string rowPath = $"{repeater.Path}/{rowIndex}";

            if (rowElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new FormResponseFieldError(
                    "INVALID_TYPE",
                    rowPath,
                    $"Repeater row {rowIndex} must be an object."));
                continue;
            }

            Dictionary<string, JsonElement> rowAnswers = [];
            foreach (JsonProperty property in rowElement.EnumerateObject())
            {
                if (!childCodes.Contains(property.Name))
                {
                    errors.Add(new FormResponseFieldError(
                        "UNKNOWN_FIELD",
                        $"{rowPath}/{property.Name}",
                        $"Unknown repeater field '{property.Name}'."));
                    continue;
                }

                rowAnswers[property.Name] = property.Value;
            }

            Dictionary<string, object?> normalizedRow = [];
            foreach (AnswerFieldDefinition child in repeater.Children)
            {
                _ = rowAnswers.TryGetValue(child.Code, out JsonElement childValue);
                ValidateScalarField(
                    child with { Path = $"{rowPath}/{child.Id}" },
                    childValue,
                    rules,
                    mode,
                    errors,
                    normalizedRow);
            }

            rows.Add(normalizedRow);
        }

        normalized[repeater.Code] = rows;
    }

    private static void ValidateRepeaterItemCount(
        AnswerFieldDefinition repeater,
        int rowCount,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors)
    {
        if (mode != FormResponseValidationMode.Complete)
        {
            return;
        }

        int minItems = repeater.Schema["minItems"]?.GetValue<int>() ?? 0;
        int? maxItems = repeater.Schema["maxItems"]?.GetValue<int>();

        if (rowCount < minItems)
        {
            errors.Add(new FormResponseFieldError(
                "REPEATER_MIN_ITEMS",
                repeater.Path,
                $"Repeater '{repeater.Code}' requires at least {minItems} items."));
        }

        if (maxItems is not null && rowCount > maxItems)
        {
            errors.Add(new FormResponseFieldError(
                "REPEATER_MAX_ITEMS",
                repeater.Path,
                $"Repeater '{repeater.Code}' allows at most {maxItems} items."));
        }
    }

    private static void ApplyCalculatedFields(
        Dictionary<string, JsonElement> answers,
        Dictionary<string, AnswerFieldDefinition> fieldsByCode,
        FormRuleEvaluationResult rules,
        Dictionary<string, object?> normalized,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors)
    {
        foreach ((string code, object? calculatedValue) in rules.CalculatedValues)
        {
            if (!fieldsByCode.TryGetValue(code, out AnswerFieldDefinition? field))
            {
                continue;
            }

            _ = answers.TryGetValue(code, out JsonElement submittedElement);
            object? submittedValue = submittedElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : JsonElementToObject(submittedElement);

            if (submittedValue is not null && !ValuesEqual(submittedValue, calculatedValue) && mode == FormResponseValidationMode.Complete)
            {
                errors.Add(new FormResponseFieldError(
                    "CALCULATED_VALUE_MISMATCH",
                    field.Path,
                    $"Field '{field.Code}' must match the server-calculated value."));
            }

            if (calculatedValue is double number && !double.IsFinite(number))
            {
                if (mode == FormResponseValidationMode.Complete)
                {
                    errors.Add(new FormResponseFieldError(
                        "CALCULATED_VALUE_INVALID",
                        field.Path,
                        $"Calculated field '{field.Code}' could not be determined from the current answers."));
                }

                continue;
            }

            if (mode == FormResponseValidationMode.Complete
                && rules.Required.TryGetValue(field.Id, out bool required)
                && required
                && IsEmpty(calculatedValue))
            {
                errors.Add(new FormResponseFieldError(
                    "REQUIRED_FIELD_MISSING",
                    field.Path,
                    $"Calculated field '{field.Code}' is required."));
            }

            normalized[code] = calculatedValue;
        }
    }

    private static void AppendCrossFieldRuleErrors(
        FormRuleEvaluationResult rules,
        List<FormResponseFieldError> errors)
    {
        foreach (RuleValidationError validationError in rules.ValidationErrors)
        {
            errors.Add(new FormResponseFieldError(
                validationError.Code,
                "/rules/validations",
                validationError.Message));
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

    private static string ResolveRulesSchema(string clinicalSchemaJson, string? rulesSchemaJson)
    {
        if (!string.IsNullOrWhiteSpace(rulesSchemaJson))
        {
            return rulesSchemaJson;
        }

        JsonObject clinicalRoot = ParseObject(clinicalSchemaJson, "clinical schema");
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
            if (fieldsByCode.TryGetValue(code, out AnswerFieldDefinition? field) && field.Type == "repeater")
            {
                if (value.ValueKind == JsonValueKind.Array)
                {
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
                else
                {
                    flat[code] = 0;
                }

                continue;
            }

            flat[code] = JsonElementToObject(value);
        }

        return flat;
    }

    private static bool TryConvertValue(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;

        switch (field.Type)
        {
            case "text":
            case "textarea":
                if (value.ValueKind != JsonValueKind.String)
                {
                    error = TypeError(field, "a string");
                    return false;
                }

                converted = value.GetString();
                return true;
            case "number":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
                {
                    error = TypeError(field, "a number");
                    return false;
                }

                converted = number;
                return true;
            case "integer":
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long integer))
                {
                    error = TypeError(field, "an integer");
                    return false;
                }

                converted = integer;
                return true;
            case "boolean":
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = TypeError(field, "a boolean");
                    return false;
                }

                converted = value.GetBoolean();
                return true;
            case "date":
                if (value.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                {
                    error = TypeError(field, "an ISO date (YYYY-MM-DD)");
                    return false;
                }

                converted = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case "datetime":
                if (value.ValueKind != JsonValueKind.String
                    || !DateTimeOffset.TryParse(
                        value.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out DateTimeOffset dateTime))
                {
                    error = TypeError(field, "an ISO date-time");
                    return false;
                }

                converted = dateTime.ToString("O", CultureInfo.InvariantCulture);
                return true;
            case "time":
                if (value.ValueKind != JsonValueKind.String
                    || !TimeOnly.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
                {
                    error = TypeError(field, "an ISO time");
                    return false;
                }

                converted = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return true;
            case "choice":
                return TryConvertChoice(field, value, out converted, out error);
            default:
                error = new FormResponseFieldError(
                    "UNSUPPORTED_FIELD_TYPE",
                    field.Path,
                    $"Field type '{field.Type}' is not supported for answer validation.");
                return false;
        }
    }

    private static bool TryConvertChoice(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;

        if (field.Schema["options"] is not JsonArray options || options.Count == 0)
        {
            error = new FormResponseFieldError(
                "INVALID_SCHEMA",
                field.Path,
                $"Choice field '{field.Code}' is missing options.");
            return false;
        }

        var allowedValues = options
            .Select(item => item?["value"]?.GetValue<string>())
            .Where(item => item is not null)
            .Select(item => item)
            .ToHashSet(StringComparer.Ordinal);

        bool allowMultiple = field.Schema["allowMultiple"]?.GetValue<bool>() ?? false;
        if (allowMultiple)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                error = TypeError(field, "an array of choice values");
                return false;
            }

            var selected = new List<string>();
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    error = TypeError(field, "an array of choice values");
                    return false;
                }

                string? choice = item.GetString();
                if (choice is null || !allowedValues.Contains(choice))
                {
                    error = new FormResponseFieldError(
                        "CONSTRAINT_VIOLATION",
                        field.Path,
                        $"Field '{field.Code}' contains an invalid choice value.");
                    return false;
                }

                selected.Add(choice);
            }

            converted = selected;
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            error = TypeError(field, "a choice value");
            return false;
        }

        string? selectedValue = value.GetString();
        if (selectedValue is null || !allowedValues.Contains(selectedValue))
        {
            error = new FormResponseFieldError(
                "CONSTRAINT_VIOLATION",
                field.Path,
                $"Field '{field.Code}' contains an invalid choice value.");
            return false;
        }

        converted = selectedValue;
        return true;
    }

    private static bool ValidateConstraints(
        AnswerFieldDefinition field,
        object value,
        out FormResponseFieldError? error)
    {
        error = null;

        return field.Type switch
        {
            "text" or "textarea" => ValidateStringConstraints(field, (string)value, out error),
            "number" => ValidateNumericConstraints(field, Convert.ToDouble(value, CultureInfo.InvariantCulture), out error),
            "integer" => ValidateNumericConstraints(field, Convert.ToInt64(value, CultureInfo.InvariantCulture), out error),
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
            error = ConstraintError(field, $"must be at least {minLength} characters.");
            return false;
        }

        if (field.Schema["maxLength"]?.GetValue<int>() is int maxLength && value.Length > maxLength)
        {
            error = ConstraintError(field, $"must be at most {maxLength} characters.");
            return false;
        }

        if (field.Schema["pattern"]?.GetValue<string>() is { Length: > 0 } pattern
            && !Regex.IsMatch(value, pattern))
        {
            error = ConstraintError(field, "does not match the required pattern.");
            return false;
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
            error = ConstraintError(field, $"must be greater than or equal to {minimum}.");
            return false;
        }

        if (field.Schema["maximum"]?.GetValue<double>() is double maximum && value > maximum)
        {
            error = ConstraintError(field, $"must be less than or equal to {maximum}.");
            return false;
        }

        if (field.Schema["multipleOf"]?.GetValue<double>() is double multipleOf
            && multipleOf != 0
            && Math.Abs((value / multipleOf) - Math.Round(value / multipleOf)) > 0.000001)
        {
            error = ConstraintError(field, $"must be a multiple of {multipleOf}.");
            return false;
        }

        return true;
    }

    private static FormResponseFieldError TypeError(AnswerFieldDefinition field, string expectedType)
    {
        return new("INVALID_TYPE", field.Path, $"Field '{field.Code}' must be {expectedType}.");
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
        else if (left is long leftInteger && right is long rightInteger)
        {
            return leftInteger == rightInteger;
        }
        else
        {
            return left is IList<string> leftChoices && right is IList<string> rightChoices
                ? leftChoices.SequenceEqual(rightChoices, StringComparer.Ordinal)
                : Equals(left, right);
        }
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
            JsonValueKind.Undefined => throw new NotImplementedException(),
            JsonValueKind.Object => throw new NotImplementedException(),
            JsonValueKind.Array => throw new NotImplementedException(),
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

    private static JsonObject ParseObject(string json, string label)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject()
                ?? throw new ValidationException($"Invalid {label}: expected a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new ValidationException($"Invalid {label}: {exception.Message}");
        }
    }

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
                string fieldPath = $"{path}/{index}";
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

                if (type is not "group")
                {
                    byCode[code] = definition;
                }

                if (field["items"] is JsonArray nestedItems && type == "group")
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
                string fieldPath = $"{path}/{index}";
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
