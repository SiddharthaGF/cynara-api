using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cynara.Application.Modules.FormAi;

public static class FormAiPromptBuilder
{
    private const int SkillMaxInlineChars = 2_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Build the system prompt for a chat turn. The header stays compact so
    /// long conversations remain small; the full skill loads only on the
    /// first turn, trimmed from the front to keep its useful tail (types,
    /// widgets, rules), and later turns reference it by short pointer.
    /// </summary>
    public static string BuildSystemPrompt(string locale, string? skillBody = null, bool isFirstTurn = true)
    {
        ArgumentNullException.ThrowIfNull(locale);
        string language = LocaleDisplayName(locale);
        var lines = new List<string>
        {
            "You are Cynara form-schema authoring agent.",
            "Respond with exactly one JSON object wrapped in a markdown fence:",
            "```json",
            "{ ... }",
            "```",
            "The opening fence must be ```json and the closing fence ```. No prose outside the fence.",
            string.Empty,
            "Output contract:",
            "- mode: patch (default) | replace (major rebuild) | unchanged (Q&A or refusal).",
            "- patch keys: clear, upsertClinicalFields, removeFieldIds, upsertUiFields, layout, upsertRulesFields, removeRulesFieldIds, upsertValidations, removeValidationCodes.",
            "- replace keys: clinical, ui, rules.",
            "- Envelope keys: summary, assistantMessage, mode, then patch or clinical/ui/rules.",
            "- Minimal diffs; never claim edits under mode=unchanged.",
            string.Empty,
            $"Write summary, assistantMessage, and user-visible labels in {language}.",
            $"User interface locale: {locale} ({language}).",
        };

        if (isFirstTurn && !string.IsNullOrWhiteSpace(skillBody))
        {
            string body = skillBody.Trim();
            if (body.Length > SkillMaxInlineChars)
            {
                body = "...(skill body trimmed; refer to last loaded version)..." +
                    Environment.NewLine +
                    body[^SkillMaxInlineChars..];
            }

            lines.Add(string.Empty);
            lines.Add("--- BEGIN form-schema-authoring skill (first turn) ---");
            lines.Add(body);
            lines.Add("--- END form-schema-authoring skill ---");
        }
        else if (!isFirstTurn)
        {
            lines.Add(string.Empty);
            lines.Add(
                "The full authoring skill (types, widgets, constraints, allowed AST ops, "
                + "refusal rules, validation checklist, example patches) was loaded in the "
                + "first turn of this conversation. Continue to apply that contract verbatim.");
        }

        return string.Join(Environment.NewLine, lines);
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
