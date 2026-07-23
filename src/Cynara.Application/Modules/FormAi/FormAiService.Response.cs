using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.Common;
using Cynara.Application.Forms;
using Cynara.Application.Schemas;

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
        string content = UnwrapMarkdownJsonFence(raw.Trim()) ?? raw.Trim();

        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return ExtractLastJsonObject(content);
        }
    }

    /// <summary>
    /// Prefers the last <c>```json</c> … <c>```</c> fence body when present so
    /// thinking prose outside the fence cannot poison parsing.
    /// </summary>
    private static string? UnwrapMarkdownJsonFence(string raw)
    {
        const string openFence = "```json";
        const string closeFence = "```";
        int searchFrom = 0;
        string? lastBody = null;
        while (searchFrom < raw.Length)
        {
            int open = raw.IndexOf(openFence, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (open < 0)
            {
                break;
            }

            int bodyStart = open + openFence.Length;
            if (bodyStart < raw.Length && raw[bodyStart] is '\r')
            {
                bodyStart++;
            }

            if (bodyStart < raw.Length && raw[bodyStart] is '\n')
            {
                bodyStart++;
            }

            int close = raw.IndexOf(closeFence, bodyStart, StringComparison.Ordinal);
            if (close < 0)
            {
                break;
            }

            lastBody = raw[bodyStart..close].Trim();
            searchFrom = close + closeFence.Length;
        }

        if (lastBody is not null)
        {
            return lastBody;
        }

        // Generic ``` … ``` wrapper with no language tag.
        if (raw.StartsWith(closeFence, StringComparison.Ordinal)
            && raw.EndsWith(closeFence, StringComparison.Ordinal)
            && raw.Length > closeFence.Length * 2)
        {
            int firstNewline = raw.IndexOf('\n', StringComparison.Ordinal);
            return firstNewline >= 0
                ? raw[(firstNewline + 1)..^closeFence.Length].Trim()
                : raw[closeFence.Length..^closeFence.Length].Trim();
        }

        return null;
    }

    private static JsonObject? ExtractLastJsonObject(string content)
    {
        JsonObject? lastEnvelope = null;
        JsonObject? lastBarePatch = null;
        for (int index = 0; index < content.Length; index++)
        {
            if (content[index] != '{')
            {
                continue;
            }

            if (!TryParseBalancedJsonObject(content, index, out JsonObject? parsed)
                || parsed is null)
            {
                continue;
            }

            index = Math.Max(index, FindBalancedObjectEnd(content, index));
            if (LooksLikeAiResponseEnvelope(parsed))
            {
                lastEnvelope = parsed;
            }
            else if (LooksLikeBarePatch(parsed))
            {
                lastBarePatch = parsed;
            }
        }

        return lastEnvelope
            ?? (lastBarePatch is null ? null : WrapBarePatch(lastBarePatch));
    }

    private static bool LooksLikeAiResponseEnvelope(JsonObject parsed)
    {
        return parsed["mode"] is not null
            || parsed["summary"] is not null
            || parsed["assistantMessage"] is not null
            || parsed["clinical"] is not null
            || parsed[AiModePatch] is not null
            || parsed["error"] is not null;
    }

    private static bool LooksLikeBarePatch(JsonObject parsed)
    {
        return parsed["upsertClinicalFields"] is not null
            || parsed["removeFieldIds"] is not null
            || parsed["upsertUiFields"] is not null
            || parsed["layout"] is not null
            || parsed["upsertRulesFields"] is not null
            || parsed["removeRulesFieldIds"] is not null
            || parsed["upsertValidations"] is not null
            || parsed["removeValidationCodes"] is not null
            || parsed["clear"] is not null;
    }

    private static JsonObject WrapBarePatch(JsonObject patch)
    {
        return new JsonObject
        {
            ["summary"] = "Updated form schemas.",
            ["assistantMessage"] = "Updated form schemas.",
            ["mode"] = AiModePatch,
            [AiModePatch] = patch.DeepClone(),
        };
    }

    private static bool TryParseBalancedJsonObject(
        string content,
        int start,
        out JsonObject? parsed)
    {
        parsed = null;
        int end = FindBalancedObjectEnd(content, start);
        if (end <= start)
        {
            return false;
        }

        try
        {
            parsed = JsonNode.Parse(content[start..(end + 1)]) as JsonObject;
            return parsed is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int FindBalancedObjectEnd(string content, int start)
    {
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int index = start; index < content.Length; index++)
        {
            char character = content[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return index;
                    }

                    break;
                default:
                    break;
            }
        }

        return -1;
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
        catch (ValidationException)
        {
            // Soften invalid AI layout/rules without discarding already-valid
            // cross-field validations from the draft.
            if (!TryValidateWithFallback(
                    schemaValidator,
                    clinical,
                    sanitized.Ui,
                    sanitized.Rules,
                    out ui,
                    out rules))
            {
                throw;
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

    private static bool TryValidateWithFallback(
        ISchemaValidator schemaValidator,
        string clinical,
        JsonObject uiObject,
        JsonObject rulesObject,
        out string ui,
        out string rules)
    {
        // 1) Drop layout only — keep field rules and validations.
        var layoutClearedUi = (JsonObject)uiObject.DeepClone();
        layoutClearedUi[SchemaJsonKeys.Layout] = new JsonArray();
        if (TryValidate(
                schemaValidator,
                clinical,
                layoutClearedUi,
                rulesObject,
                out ui,
                out rules))
        {
            return true;
        }

        // 2) Drop field rules only — keep validations.
        var fieldsClearedRules = (JsonObject)rulesObject.DeepClone();
        fieldsClearedRules[SchemaJsonKeys.Fields] = new JsonObject();
        if (TryValidate(
                schemaValidator,
                clinical,
                layoutClearedUi,
                fieldsClearedRules,
                out ui,
                out rules))
        {
            return true;
        }

        // 3) Last resort: empty rules validations too.
        fieldsClearedRules[SchemaJsonKeys.Validations] = new JsonArray();
        return TryValidate(
            schemaValidator,
            clinical,
            layoutClearedUi,
            fieldsClearedRules,
            out ui,
            out rules);
    }

    private static bool TryValidate(
        ISchemaValidator schemaValidator,
        string clinical,
        JsonObject uiObject,
        JsonObject rulesObject,
        out string ui,
        out string rules)
    {
        ui = uiObject.ToJsonString();
        rules = rulesObject.ToJsonString();
        try
        {
            schemaValidator.ValidateFormDraft(clinical, ui, rules);
            return true;
        }
        catch (ValidationException)
        {
            return false;
        }
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
