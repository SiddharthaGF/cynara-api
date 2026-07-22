using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

public static class FormAiPromptBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static string BuildSystemPrompt(string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        string language = LocaleDisplayName(locale);
        return string.Join(
            '\n',
            "You are Cynara form-schema authoring agent.",
            "Respond with a single JSON object only (no markdown fences).",
            "",
            "Prefer mode patch with minimal upserts/removes. Use unchanged for Q&A, refusals, unsupported capabilities, or no edit. Use replace only for major rebuilds.",
            "The response keys should be summary, assistantMessage, mode, then patch or the full clinical/ui/rules triple.",
            "A patch may contain clear, upsertClinicalFields, removeFieldIds, upsertUiFields, layout, upsertRulesFields, removeRulesFieldIds, upsertValidations, and removeValidationCodes.",
            "Clear the draft with mode patch and patch.clear=true.",
            "",
            "Scope: author and correct the open clinical form. Do not browse, use tools, run code, call external APIs, or answer unrelated questions.",
            "Unsupported types, widgets, operators, file uploads, signatures, network lookups, and product actions must use mode unchanged and explain the limitation in plain language.",
            "Keep clinical constraints in clinical, presentation in ui, and runtime behavior in rules.",
            "Supported types: text, textarea, number, integer, boolean, date, datetime, time, choice, group, repeater, component-ref.",
            "Rules use only declarative AST nodes ref, lit, op+args. Allowed ops: eq, neq, gt, gte, lt, lte, and, or, not, empty, coalesce, add, sub, mul, div.",
            "Calculated targets must be clinical readOnly=true. Cross-field validations contain only code, message, assert, and optional when.",
            "Preserve existing id and code on corrections. IDs are lowercase kebab-case; clinical codes are stable. UI field keys and layout fieldIds reference clinical ids; rule refs use clinical codes.",
            "ui.clinicalSchemaVersion and rules.clinicalSchemaVersion must equal clinical.schemaVersion.",
            "Write summary, assistantMessage, and user-visible labels in " + language + ". Keep identifiers in English.",
            "Never expose JSON keys, schema paths, or implementation details in assistantMessage.",
            $"User interface locale: {locale} ({language}).");
    }

    public static string BuildUserTurn(
        string formCode,
        string locale,
        IReadOnlyList<FormAiChatMessage> messages,
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        IReadOnlyList<FocusedField> focusedFields,
        IReadOnlyList<FocusedFieldType> focusedFieldTypes)
    {
        ArgumentNullException.ThrowIfNull(formCode);
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(clinicalSchemaJson);
        ArgumentNullException.ThrowIfNull(focusedFields);
        ArgumentNullException.ThrowIfNull(focusedFieldTypes);
        FormAiChatMessage? latestUser = messages.LastOrDefault(
            message => string.Equals(message.Role, "user", StringComparison.Ordinal));
        var payload = new
        {
            formCode,
            locale,
            language = LocaleDisplayName(locale),
            latestUserMessage = latestUser?.Content ?? string.Empty,
            conversation = messages.TakeLast(6),
            focusedFields,
            focusedFieldTypes,
            currentDraft = new
            {
                clinical = SafeParse(clinicalSchemaJson),
                ui = SafeParse(uiSchemaJson),
                rules = SafeParse(rulesSchemaJson),
            },
            instruction = "Apply the request with mode patch whenever possible. Use unchanged for questions or refusals. Use replace only for major rebuilds. Reply in the requested language and keep the designer message short.",
        };
        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static string LocaleDisplayName(string locale)
    {
        if (locale.StartsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return "Spanish";
        }
        else if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "English";
        }
        else
        {
            return string.IsNullOrWhiteSpace(locale) ? "English" : locale;
        }
    }

    private static JsonNode? SafeParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return new JsonObject { ["parseError"] = true };
        }
    }
}

public sealed record FocusedField(
    string Id,
    JsonNode? Clinical,
    JsonNode? Ui,
    JsonNode? Rules);

public sealed record FocusedFieldType(string Type, IReadOnlyList<string> Aliases);
