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
            string.Empty,
            "The full authoring contract (types, constraints, widgets, allowed ops, refusals, example patches, decision gates, validation checklist, JSON assets for the canonical widget map, rules examples, and output template) is loaded below as the canonical skill body. Treat it as authoritative — it overrides any shorthand in this header.",
        };
        if (!string.IsNullOrWhiteSpace(skillBody))
        {
            lines.Add(string.Empty);
            lines.Add("--- BEGIN form-schema-authoring skill ---");
            lines.Add(skillBody.Trim());
            lines.Add("--- END form-schema-authoring skill ---");
        }

        lines.Add(string.Empty);
        lines.Add("Output contract reminder:");
        lines.Add("Prefer mode patch with minimal upserts/removes. Use unchanged for Q&A, refusals, and partial offers without user acceptance. Use replace only for major rebuilds.");
        lines.Add("Keys: summary, assistantMessage, mode, then patch (mode=patch) or clinical+ui+rules (mode=replace).");
        lines.Add("Patch may contain clear, upsertClinicalFields, removeFieldIds, upsertUiFields, layout, upsertRulesFields, removeRulesFieldIds, upsertValidations, removeValidationCodes.");
        lines.Add($"Write summary, assistantMessage, and user-visible labels in {language}. Keep identifiers in English.");
        lines.Add("Never expose JSON keys, schema paths, or implementation details in assistantMessage.");
        lines.Add($"User interface locale: {locale} ({language}).");
        return string.Join('\n', lines);
    }

    public static string BuildUserTurn(FormAiUserTurnRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        FormAiChatMessage? latestUser = request.Messages.LastOrDefault(
            message => string.Equals(message.Role, "user", StringComparison.Ordinal));
        var payload = new
        {
            formCode = request.FormCode,
            locale = request.Locale,
            language = LocaleDisplayName(request.Locale),
            latestUserMessage = latestUser?.Content ?? string.Empty,
            conversation = request.Messages.TakeLast(6),
            focusedFields = request.FocusedFields,
            focusedFieldTypes = request.FocusedFieldTypes,
            currentDraft = new
            {
                clinical = SafeParse(request.ClinicalSchemaJson),
                ui = SafeParse(request.UiSchemaJson),
                rules = SafeParse(request.RulesSchemaJson),
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

        if (locale.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return "English";
        }

        return string.IsNullOrWhiteSpace(locale) ? "English" : locale;
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

public sealed record FormAiUserTurnRequest(
    string FormCode,
    string Locale,
    IReadOnlyList<FormAiChatMessage> Messages,
    string ClinicalSchemaJson,
    string? UiSchemaJson,
    string? RulesSchemaJson,
    IReadOnlyList<FocusedField> FocusedFields,
    IReadOnlyList<FocusedFieldType> FocusedFieldTypes);

public sealed record FocusedField(
    string Id,
    JsonNode? Clinical,
    JsonNode? Ui,
    JsonNode? Rules);

public sealed record FocusedFieldType(string Type, IReadOnlyList<string> Aliases);
