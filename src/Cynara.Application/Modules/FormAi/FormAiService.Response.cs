using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Forms;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static FormAiChatMessage RequireLatestUser(IReadOnlyList<FormAiChatMessage> messages)
    {
        FormAiChatMessage? latest = messages.LastOrDefault(
            item => string.Equals(item.Role, "user", StringComparison.Ordinal));
        return latest ?? throw new ValidationException("At least one user message is required.");
    }

    private static List<FormAiChatMessage> NormalizeMessages(
        IReadOnlyList<FormAiChatMessage>? messages)
    {
        if (messages is null)
        {
            throw new ValidationException("At least one user message is required.");
        }

        var result = messages
            .Where(item => (item.Role is "user" or "assistant") && !string.IsNullOrWhiteSpace(item.Content))
            .Select(item => new FormAiChatMessage(item.Role, item.Content.Trim()))
            .TakeLast(MaxMessages)
            .ToList();
        return result.Count == 0 || result.TrueForAll(item => !string.Equals(item.Role, "user", StringComparison.Ordinal))
            ? throw new ValidationException("At least one user message is required.")
            : result;
    }

    private static string NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return "en";
        }

        string normalized = locale.Trim().ToUpperInvariant().Replace('_', '-');
        if (normalized.StartsWith("ES", StringComparison.Ordinal))
        {
            return "es";
        }

        return normalized.StartsWith("EN", StringComparison.Ordinal)
            ? "en"
            : normalized[..Math.Min(16, normalized.Length)];
    }

    private static FormAiChatResponse LimitationResponse(
        DraftContext draft,
        string message,
        string locale)
    {
        return new FormAiChatResponse(
            FormAiGuardrails.LimitationSummary(locale),
            message,
            Thinking: null,
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson ?? DefaultUiSchema,
            draft.RulesSchemaJson ?? DefaultRulesSchema);
    }

    private static FormAiChatResponse EmptyDraftResponse(string locale)
    {
        DraftTriple empty = FormAiDraftPatch.Empty();
        return new FormAiChatResponse(
            FormAiGuardrails.DraftResetSummary(locale),
            FormAiGuardrails.DraftResetMessage(locale),
            Thinking: null,
            empty.Clinical.ToJsonString(),
            empty.Ui.ToJsonString(),
            empty.Rules.ToJsonString());
    }

    private static DraftTriple ParseDraftTriple(DraftContext draft)
    {
        return new DraftTriple(
            ParseObjectOrEmpty(draft.ClinicalSchemaJson),
            ParseObjectOrEmpty(draft.UiSchemaJson),
            ParseObjectOrEmpty(draft.RulesSchemaJson));
    }

    private static JsonObject? ExtractJsonObject(string raw)
    {
        string content = raw.Trim();
        if (content.StartsWith("```", StringComparison.Ordinal) && content.EndsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = content.IndexOf('\n', StringComparison.Ordinal);
            content = firstNewline >= 0
                ? content[(firstNewline + 1)..^3].Trim()
                : content[3..^3].Trim();
        }

        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            int start = content.IndexOf('{', StringComparison.Ordinal);
            int end = content.LastIndexOf('}');
            return start >= 0 && end > start
                ? JsonNode.Parse(content[start..(end + 1)]) as JsonObject
                : null;
        }
    }

    private static JsonObject ParseObjectOrEmpty(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? [] : ExtractJsonObject(json) ?? [];
    }

    private static string? ReadText(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static string ResolveMode(JsonObject parsed)
    {
        if (parsed[AiModePatch] is JsonObject)
        {
            return AiModePatch;
        }

        return parsed["clinical"] is JsonObject
                    && parsed["ui"] is JsonObject
                    && parsed["rules"] is JsonObject
            ? "replace"
            : "unchanged";
    }

    private static bool IsValidFieldId(string value)
    {
        return FieldIdRegex.IsMatch(value.Trim());
    }

    private async Task<DraftContext> ResolveDraftAsync(
        string formCode,
        FormAiChatRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ClinicalSchemaJson))
        {
            return new DraftContext(
                request.ClinicalSchemaJson,
                request.UiSchemaJson,
                request.RulesSchemaJson);
        }

        FormVersionDto draft = await forms.GetEditableVersionAsync(formCode, cancellationToken).ConfigureAwait(false);
        return new DraftContext(
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson,
            draft.RulesSchemaJson);
    }

    private IReadOnlyList<OpenAiMessage> BuildMessages(
        string formCode,
        string locale,
        IReadOnlyList<FormAiChatMessage> messages,
        DraftContext draft,
        FocusContext focus)
    {
        string skillBody = skillLoader.GetSkillBody();
        return
        [
            new("system", FormAiPromptBuilder.BuildSystemPrompt(locale, skillBody)),
            new(
                "user",
                FormAiPromptBuilder.BuildUserTurn(
                    new FormAiUserTurnRequest(
                        formCode,
                        locale,
                        messages,
                        draft.ClinicalSchemaJson,
                        draft.UiSchemaJson,
                        draft.RulesSchemaJson,
                        focus.Fields,
                        focus.Types))),
        ];
    }

    private FormAiChatResponse PrepareResponse(
        ParsedAiOutput parsed,
        DraftContext draft,
        string? thinking)
    {
        if (parsed.LimitationOnly)
        {
            return new FormAiChatResponse(
                parsed.Summary,
                parsed.AssistantMessage,
                thinking,
                draft.ClinicalSchemaJson,
                draft.UiSchemaJson ?? DefaultUiSchema,
                draft.RulesSchemaJson ?? DefaultRulesSchema);
        }

        SanitizedAiTriple sanitized = FormAiSanitizer.Sanitize(
            parsed.Triple.Clinical,
            parsed.Triple.Ui,
            parsed.Triple.Rules);
        string clinical = sanitized.Clinical.ToJsonString();
        string ui = sanitized.Ui.ToJsonString();
        string rules = sanitized.Rules.ToJsonString();
        try
        {
            schemaValidator.ValidateFormDraft(clinical, ui, rules);
        }
        catch (ValidationException firstError)
        {
            var fallbackUi = (JsonObject)sanitized.Ui.DeepClone();
            fallbackUi[SchemaJsonKeys.Layout] = new JsonArray();
            var fallbackRules = (JsonObject)sanitized.Rules.DeepClone();
            fallbackRules[SchemaJsonKeys.Fields] = new JsonObject();
            fallbackRules[SchemaJsonKeys.Validations] = new JsonArray();
            ui = fallbackUi.ToJsonString();
            rules = fallbackRules.ToJsonString();
            try
            {
                schemaValidator.ValidateFormDraft(clinical, ui, rules);
            }
            catch
            {
                throw firstError;
            }
        }

        return new FormAiChatResponse(
            parsed.Summary,
            parsed.AssistantMessage,
            thinking,
            clinical,
            ui,
            rules);
    }

    private sealed record DraftContext(
        string ClinicalSchemaJson,
        string? UiSchemaJson,
        string? RulesSchemaJson);

    private sealed record FocusContext(
        IReadOnlyList<FocusedField> Fields,
        IReadOnlyList<FocusedFieldType> Types);

    private sealed record StreamPartialState(
        StringBuilder RawContent,
        StringBuilder Thinking,
        int EmittedMessageLength,
        bool MessagePhaseSent);

    private sealed record ParsedAiOutput(
        string Summary,
        string AssistantMessage,
        DraftTriple Triple,
        bool LimitationOnly)
    {
        public static ParsedAiOutput Unchanged(
            string summary,
            string message,
            DraftContext draft)
        {
            return new ParsedAiOutput(
                summary,
                message,
                new DraftTriple(
                    ParseObjectOrEmpty(draft.ClinicalSchemaJson),
                    ParseObjectOrEmpty(draft.UiSchemaJson),
                    ParseObjectOrEmpty(draft.RulesSchemaJson)),
                LimitationOnly: true);
        }
    }

    private sealed record PartialStringField(string Value, bool Complete);
}
