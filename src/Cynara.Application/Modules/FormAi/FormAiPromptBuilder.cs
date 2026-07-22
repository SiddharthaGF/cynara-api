using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

public static class FormAiPromptBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static string BuildSystemPrompt(string locale, string? skillBody = null)
    {
        ArgumentNullException.ThrowIfNull(locale);
        string language = LocaleDisplayName(locale);
        var lines = new List<string>
        {
            "You are Cynara form-schema authoring agent.",
            "Respond with a single JSON object only (no markdown fences).",
            "",
            "The full authoring contract (types, constraints, widgets, allowed ops, refusals, example patches, decision gates, validation checklist, JSON assets for the canonical widget map, rules examples, and output template) is loaded below as the canonical skill body. Treat it as authoritative — it overrides any shorthand in this header.",
        };
        if (!string.IsNullOrWhiteSpace(skillBody))
        {
            lines.Add("");
            lines.Add("--- BEGIN form-schema-authoring skill ---");
            lines.Add(skillBody.Trim());
            lines.Add("--- END form-schema-authoring skill ---");
        }
        lines.Add("");
        lines.Add("Output contract reminder:");
        lines.Add("Prefer mode patch with minimal upserts/removes. Use unchanged for Q&A, refusals, and partial offers without user acceptance. Use replace only for major rebuilds.");
        lines.Add("Keys: summary, assistantMessage, mode, then patch (mode=patch) or clinical+ui+rules (mode=replace).");
        lines.Add("Patch may contain clear, upsertClinicalFields, removeFieldIds, upsertUiFields, layout, upsertRulesFields, removeRulesFieldIds, upsertValidations, removeValidationCodes.");
        lines.Add($"Write summary, assistantMessage, and user-visible labels in {language}. Keep identifiers in English.");
        lines.Add("Never expose JSON keys, schema paths, or implementation details in assistantMessage.");
        lines.Add($"User interface locale: {locale} ({language}).");
        return string.Join('\n', lines);
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
