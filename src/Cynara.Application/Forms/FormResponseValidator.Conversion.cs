using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Forms;

public sealed partial class FormResponseValidator
{
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

    private static bool TryConvertValue(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;

        return field.Type switch
        {
            FieldTypeNames.Text or FieldTypeNames.Textarea =>
                TryConvertString(field, value, out converted, out error),
            FieldTypeNames.Number =>
                TryConvertNumber(field, value, out converted, out error),
            FieldTypeNames.Integer =>
                TryConvertInteger(field, value, out converted, out error),
            FieldTypeNames.Boolean =>
                TryConvertBoolean(field, value, out converted, out error),
            FieldTypeNames.Date =>
                TryConvertDate(field, value, out converted, out error),
            FieldTypeNames.DateTime =>
                TryConvertDateTime(field, value, out converted, out error),
            FieldTypeNames.Time =>
                TryConvertTime(field, value, out converted, out error),
            FieldTypeNames.Choice =>
                TryConvertChoice(field, value, out converted, out error),
            _ => RejectUnsupportedFieldType(field, out error),
        };
    }

    private static bool RejectUnsupportedFieldType(
        AnswerFieldDefinition field,
        out FormResponseFieldError? error)
    {
        error = new FormResponseFieldError(
            "UNSUPPORTED_FIELD_TYPE",
            field.Path,
            $"Field type '{field.Type}' is not supported for answer validation.");
        return false;
    }

    private static bool TryConvertString(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind != JsonValueKind.String)
        {
            error = TypeError(field, "a string");
            return false;
        }

        converted = value.GetString();
        return true;
    }

    private static bool TryConvertNumber(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out double number))
        {
            error = TypeError(field, "a number");
            return false;
        }

        converted = number;
        return true;
    }

    private static bool TryConvertInteger(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out long integer))
        {
            error = TypeError(field, "an integer");
            return false;
        }

        converted = integer;
        return true;
    }

    private static bool TryConvertBoolean(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = TypeError(field, "a boolean");
            return false;
        }

        converted = value.GetBoolean();
        return true;
    }

    private static bool TryConvertDate(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly date))
        {
            error = TypeError(field, "an ISO date (YYYY-MM-DD)");
            return false;
        }

        converted = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryConvertDateTime(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
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
    }

    private static bool TryConvertTime(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;
        if (value.ValueKind != JsonValueKind.String
            || !TimeOnly.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out TimeOnly time))
        {
            error = TypeError(field, "an ISO time");
            return false;
        }

        converted = time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryConvertChoice(
        AnswerFieldDefinition field,
        JsonElement value,
        out object? converted,
        out FormResponseFieldError? error)
    {
        if (field.Schema[SchemaJsonKeys.Options] is not JsonArray options
            || options.Count == 0)
        {
            converted = null;
            error = new FormResponseFieldError(
                "INVALID_SCHEMA",
                field.Path,
                $"Choice field '{field.Code}' is missing options.");
            return false;
        }

        HashSet<string> allowedValues = BuildAllowedChoiceValues(options);
        bool allowMultiple = field.Schema["allowMultiple"]?.GetValue<bool>() ?? false;
        return allowMultiple
            ? TryConvertMultiChoice(
                field,
                value,
                allowedValues,
                out converted,
                out error)
            : TryConvertSingleChoice(
                field,
                value,
                allowedValues,
                out converted,
                out error);
    }

    private static HashSet<string> BuildAllowedChoiceValues(JsonArray options)
    {
        return options
            .Select(item => item?["value"]?.GetValue<string>())
            .Where(item => item is not null)
            .Select(item => item!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryConvertMultiChoice(
        AnswerFieldDefinition field,
        JsonElement value,
        HashSet<string> allowedValues,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;

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

    private static bool TryConvertSingleChoice(
        AnswerFieldDefinition field,
        JsonElement value,
        HashSet<string> allowedValues,
        out object? converted,
        out FormResponseFieldError? error)
    {
        converted = null;
        error = null;

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

    private static FormResponseFieldError TypeError(AnswerFieldDefinition field, string expectedType)
    {
        return new("INVALID_TYPE", field.Path, $"Field '{field.Code}' must be {expectedType}.");
    }
}
