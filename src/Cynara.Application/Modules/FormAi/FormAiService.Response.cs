using System.Text;
using System.Text.Json.Nodes;

using Cynara.Application.Forms;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static readonly string[] LayoutOnly = ["layout"];
    private static readonly string[] LayoutAndRulesFields = ["layout", "rules.fields"];
    private static readonly string[] LayoutRulesFieldsAndValidations =
        ["layout", "rules.fields", "rules.validations"];

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
        // Only load the full skill body on the first turn of a conversation.
        // Subsequent turns reuse the contract that was already loaded — the
        // header reminder explicitly tells the model to keep applying it.
        bool isFirstTurn = messages.Count <= 1;
        string? skillBody = isFirstTurn ? skillLoader.GetSkillBody() : null;
        return
        [
            new(
                "system",
                FormAiPromptBuilder.BuildSystemPrompt(locale, skillBody, isFirstTurn)),
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
        string? thinking,
        out FormAiFallbackReport fallback)
    {
        if (parsed.LimitationOnly)
        {
            fallback = FormAiFallbackReport.NoFallback;
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
        fallback = FormAiFallbackReport.NoFallback;
        try
        {
            schemaValidator.ValidateFormDraft(clinical, ui, rules);
        }
        catch (ValidationException)
        {
            _ = TryValidateWithFallback(
                schemaValidator,
                clinical,
                sanitized.Ui,
                sanitized.Rules,
                out ui,
                out rules,
                out fallback);
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
        bool MessagePhaseSent,
        FormAiFallbackReport? Fallback = null,
        bool IsTruncated = false);

    private sealed record ParsedAiOutput(
        string Summary,
        string AssistantMessage,
        DraftTriple Triple,
        bool LimitationOnly,
        bool IsRefusal = false)
    {
        public static ParsedAiOutput Unchanged(
            string summary,
            string message,
            DraftContext draft,
            bool isRefusal = false)
        {
            return new ParsedAiOutput(
                summary,
                message,
                new DraftTriple(
                    ParseObjectOrEmpty(draft.ClinicalSchemaJson),
                    ParseObjectOrEmpty(draft.UiSchemaJson),
                    ParseObjectOrEmpty(draft.RulesSchemaJson)),
                LimitationOnly: true,
                IsRefusal: isRefusal);
        }
    }

    private sealed record PartialStringField(string Value, bool Complete);
}
