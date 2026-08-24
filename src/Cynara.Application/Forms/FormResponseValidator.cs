using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Forms;

public sealed partial class FormResponseValidator(IFormRuleEngine ruleEngine) : IFormResponseValidator
{
    public FormResponseValidationResult Validate(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        string answersJson,
        FormResponseValidationMode mode)
    {
        JsonObject clinicalRoot = JsonParsing.ParseObject(clinicalSchemaJson, "clinical schema");
        Dictionary<string, AnswerFieldDefinition> fieldsByCode = AnswerFieldIndex.Build(clinicalRoot);
        var repeatersByCode = fieldsByCode.Values
            .Where(static item => string.Equals(
                item.Type,
                FieldTypeNames.Repeater,
                StringComparison.Ordinal))
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
            if (field.Type is FieldTypeNames.Group or FieldTypeNames.Repeater or FieldTypeNames.ComponentRef)
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
            .Where(item => item.Type is not (FieldTypeNames.Group or FieldTypeNames.ComponentRef))
            .Select(item => item.Code)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in answers.Keys.Where(key => !allowed.Contains(key)))
        {
            errors.Add(new FormResponseFieldError(
                "UNKNOWN_FIELD",
                $"/answers/{key}",
                $"Unknown answer field '{key}'."));
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
        if (rules.CalculatedValues.ContainsKey(field.Code))
        {
            return;
        }

        (bool visible, bool enabled, bool required) = ResolveFieldFlags(field, rules);

        if (TryRejectInactiveValue(field, value, visible, enabled, errors)
            || TryRejectReadOnlyValue(field, value, rules, errors)
            || TryRejectRequiredEmpty(field, value, mode, required, errors))
        {
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

    private static (bool Visible, bool Enabled, bool Required) ResolveFieldFlags(
        AnswerFieldDefinition field,
        FormRuleEvaluationResult rules)
    {
        bool visible = !rules.Visibility.TryGetValue(field.Id, out bool isVisible) || isVisible;
        bool enabled = !rules.Enabled.TryGetValue(field.Id, out bool isEnabled) || isEnabled;
        bool required = (rules.Required.TryGetValue(field.Id, out bool isRequired) && isRequired)
            || field.Required;
        return (visible, enabled, required);
    }

    private static bool TryRejectInactiveValue(
        AnswerFieldDefinition field,
        JsonElement value,
        bool visible,
        bool enabled,
        List<FormResponseFieldError> errors)
    {
        if (visible && enabled)
        {
            return false;
        }

        if (!IsEmpty(value))
        {
            errors.Add(new FormResponseFieldError(
                visible ? "DISABLED_FIELD_VALUE" : "HIDDEN_FIELD_VALUE",
                field.Path,
                $"Field '{field.Code}' cannot accept values while hidden or disabled."));
        }

        return true;
    }

    private static bool TryRejectReadOnlyValue(
        AnswerFieldDefinition field,
        JsonElement value,
        FormRuleEvaluationResult rules,
        List<FormResponseFieldError> errors)
    {
        if (!field.ReadOnly || rules.CalculatedValues.ContainsKey(field.Code))
        {
            return false;
        }

        if (!IsEmpty(value))
        {
            errors.Add(new FormResponseFieldError(
                "READONLY_FIELD_MODIFIED",
                field.Path,
                $"Field '{field.Code}' is read-only."));
        }

        return true;
    }

    private static bool TryRejectRequiredEmpty(
        AnswerFieldDefinition field,
        JsonElement value,
        FormResponseValidationMode mode,
        bool required,
        List<FormResponseFieldError> errors)
    {
        if (!IsEmpty(value))
        {
            return false;
        }

        if (mode == FormResponseValidationMode.Complete && required)
        {
            errors.Add(new FormResponseFieldError(
                "REQUIRED_FIELD_MISSING",
                field.Path,
                $"Field '{field.Code}' is required."));
        }

        return true;
    }

    private static void ValidateRepeater(
        AnswerFieldDefinition repeater,
        JsonElement rawRows,
        FormRuleEvaluationResult rules,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors,
        Dictionary<string, object?> normalized)
    {
        if (!GateRepeaterAccess(
                repeater,
                ref rawRows,
                rules,
                mode,
                errors,
                normalized))
        {
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
            ValidateRepeaterRow(
                repeater,
                rawRows[rowIndex],
                rowIndex,
                childCodes,
                new RepeaterRowContext(rules, mode, errors, rows));
        }

        normalized[repeater.Code] = rows;
    }

    private static bool GateRepeaterAccess(
        AnswerFieldDefinition repeater,
        ref JsonElement rawRows,
        FormRuleEvaluationResult rules,
        FormResponseValidationMode mode,
        List<FormResponseFieldError> errors,
        Dictionary<string, object?> normalized)
    {
        bool visible = !rules.Visibility.TryGetValue(repeater.Id, out bool isVisible)
            || isVisible;
        bool enabled = !rules.Enabled.TryGetValue(repeater.Id, out bool isEnabled)
            || isEnabled;

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
            return false;
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
            return false;
        }

        return true;
    }

    private static void ValidateRepeaterRow(
        AnswerFieldDefinition repeater,
        JsonElement rowElement,
        int rowIndex,
        HashSet<string> childCodes,
        RepeaterRowContext context)
    {
        string rowPath = string.Create(
            CultureInfo.InvariantCulture,
            $"{repeater.Path}/{rowIndex}");

        if (rowElement.ValueKind != JsonValueKind.Object)
        {
            context.Errors.Add(new FormResponseFieldError(
                "INVALID_TYPE",
                rowPath,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Repeater row {rowIndex} must be an object.")));
            return;
        }

        Dictionary<string, JsonElement> rowAnswers = [];
        foreach (JsonProperty property in rowElement.EnumerateObject())
        {
            if (!childCodes.Contains(property.Name))
            {
                context.Errors.Add(new FormResponseFieldError(
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
                context.Rules,
                context.Mode,
                context.Errors,
                normalizedRow);
        }

        context.Rows.Add(normalizedRow);
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
                string.Create(CultureInfo.InvariantCulture, $"Repeater '{repeater.Code}' requires at least {minItems} items.")));
        }

        if (maxItems is not null && rowCount > maxItems)
        {
            errors.Add(new FormResponseFieldError(
                "REPEATER_MAX_ITEMS",
                repeater.Path,
                string.Create(CultureInfo.InvariantCulture, $"Repeater '{repeater.Code}' allows at most {maxItems} items.")));
        }
    }
}
