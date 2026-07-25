using System.Text;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService
{
    private static readonly string[] LayoutOnly = [FormAiDraftPatch.LayerLayout];

    private static readonly string[] LayoutAndRulesFields =
        [FormAiDraftPatch.LayerLayout, FormAiDraftPatch.LayerRulesFields];

    private static readonly string[] LayoutRulesFieldsAndValidations =
        [FormAiDraftPatch.LayerLayout, FormAiDraftPatch.LayerRulesFields, FormAiDraftPatch.LayerRulesValidations];

    private FormAiChatResponse PrepareResponse(
        ParsedAiOutput parsed,
        DraftContext draft,
        string? thinking,
        out FormAiFallbackReport fallback)
    {
        return parsed.LimitationOnly
            ? BuildUnchangedResponse(parsed, draft, thinking, out fallback)
            : BuildSanitizedResponse(parsed, thinking, out fallback);
    }

    private static FormAiChatResponse BuildUnchangedResponse(
        ParsedAiOutput parsed,
        DraftContext draft,
        string? thinking,
        out FormAiFallbackReport fallback)
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

    private FormAiChatResponse BuildSanitizedResponse(
        ParsedAiOutput parsed,
        string? thinking,
        out FormAiFallbackReport fallback)
    {
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
}
