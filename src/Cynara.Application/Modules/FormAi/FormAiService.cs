using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Cynara.Application.Forms;
using Cynara.Application.Schemas;

namespace Cynara.Application.Modules.FormAi;

public sealed partial class FormAiService(
    IFormService forms,
    IOpenAiClient openAi,
    IAiProviderSettingsService settings,
    ISchemaValidator schemaValidator,
    IFormAiSkillLoader skillLoader) : IFormAiService
{
    private const int MaxMessages = 24;
    private const int MaxFocusedFields = 12;

    public async Task<FormAiStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        FormAiSettingsResponse view = await settings.GetPublicViewAsync(cancellationToken).ConfigureAwait(false);
        return new FormAiStatusResponse(
            view.Configured,
            view.Configured ? view.Model : null,
            view.BaseUrl,
            view.ApiKeyConfigured,
            view.ApiKeyMasked,
            view.JsonObject,
            view.Source,
            view.BaseUrlConfigured);
    }

    public Task<FormAiSettingsResponse> GetSettingsAsync(
        CancellationToken cancellationToken)
    {
        return settings.GetPublicViewAsync(cancellationToken);
    }

    public Task<FormAiSettingsResponse> UpdateSettingsAsync(
        FormAiSettingsUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return settings.UpsertAsync(request, cancellationToken);
    }

    public async Task<FormAiChatResponse> ChatAsync(
        string formCode,
        FormAiChatRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formCode);
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<FormAiChatMessage> messages = NormalizeMessages(request.Messages);
        FormAiChatMessage latestUser = RequireLatestUser(messages);
        DraftContext draft = await ResolveDraftAsync(formCode, request, cancellationToken).ConfigureAwait(false);
        string locale = NormalizeLocale(request.Locale);

        FormAiGuardViolation? guard = FormAiGuardrails.Detect(latestUser.Content, locale);
        if (guard is not null)
        {
            return LimitationResponse(draft, guard.Message, locale);
        }

        if (FormAiGuardrails.IsDraftReset(latestUser.Content))
        {
            return EmptyDraftResponse(locale);
        }

        FocusContext focus = BuildFocusContext(request, latestUser.Content, draft);
        OpenAiConfig config = await settings.ResolveActiveConfigAsync(cancellationToken).ConfigureAwait(false);
        OpenAiCompletionResult completion = await openAi.CreateChatCompletionAsync(
            BuildMessages(formCode, locale, messages, draft, focus),
            config,
            formCode,
            cancellationToken).ConfigureAwait(false);
        ParsedAiOutput parsed = ParseModelOutput(completion.Content, draft, locale);
        return PrepareResponse(parsed, draft, completion.Thinking);
    }

    public async Task ChatStreamAsync(
        string formCode,
        FormAiChatRequest request,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formCode);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        try
        {
            IReadOnlyList<FormAiChatMessage> messages = NormalizeMessages(request.Messages);
            FormAiChatMessage latestUser = RequireLatestUser(messages);
            DraftContext draft = await ResolveDraftAsync(formCode, request, cancellationToken).ConfigureAwait(false);
            string locale = NormalizeLocale(request.Locale);
            FormAiGuardViolation? guard = FormAiGuardrails.Detect(latestUser.Content, locale);
            if (guard is not null)
            {
                FormAiChatResponse limited = LimitationResponse(draft, guard.Message, locale);
                await WriteSseAsync(output, new { type = "message", delta = limited.AssistantMessage }, cancellationToken).ConfigureAwait(false);
                await WriteSseAsync(output, new { type = "done", result = limited }, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (FormAiGuardrails.IsDraftReset(latestUser.Content))
            {
                FormAiChatResponse empty = EmptyDraftResponse(locale);
                await WriteSseAsync(output, new { type = "message", delta = empty.AssistantMessage }, cancellationToken).ConfigureAwait(false);
                await WriteSseAsync(output, new { type = "done", result = empty }, cancellationToken).ConfigureAwait(false);
                return;
            }

            FocusContext focus = BuildFocusContext(request, latestUser.Content, draft);
            OpenAiConfig config = await settings.ResolveActiveConfigAsync(cancellationToken).ConfigureAwait(false);
            string rawContent = string.Empty;
            string thinking = string.Empty;
            int emittedMessageLength = 0;
            bool messagePhaseSent = false;
            bool schemaPhaseSent = false;

            await foreach (OpenAiStreamDelta delta in openAi.StreamChatCompletionAsync(
                               BuildMessages(formCode, locale, messages, draft, focus),
                               config,
                               formCode,
                               cancellationToken).ConfigureAwait(false))
            {
                rawContent += delta.Content ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(delta.Reasoning))
                {
                    thinking += delta.Reasoning;
                }

                if (rawContent.Length > 0)
                {
                    PartialStringField? partial = ExtractPartialJsonStringField(
                        rawContent,
                        "assistantMessage");
                    if (partial is not null)
                    {
                        if (!messagePhaseSent)
                        {
                            messagePhaseSent = true;
                            await WriteSseAsync(output, new { type = "phase", phase = "message" }, cancellationToken).ConfigureAwait(false);
                        }

                        if (partial.Value.Length > emittedMessageLength)
                        {
                            string chunk = partial.Value[emittedMessageLength..];
                            emittedMessageLength = partial.Value.Length;
                            await WriteSseAsync(output, new { type = "message", delta = chunk }, cancellationToken).ConfigureAwait(false);
                        }

                        if (partial.Complete && !schemaPhaseSent)
                        {
                            schemaPhaseSent = true;
                            await WriteSseAsync(output, new { type = "phase", phase = "schema" }, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }

            OpenAiCompletionResult completion = new(rawContent, thinking.Length == 0 ? null : thinking);
            ParsedAiOutput parsed = ParseModelOutput(completion.Content, draft, locale);
            FormAiChatResponse result = PrepareResponse(parsed, draft, completion.Thinking);
            if (emittedMessageLength == 0 && result.AssistantMessage.Length > 0)
            {
                if (!messagePhaseSent)
                {
                    await WriteSseAsync(output, new { type = "phase", phase = "message" }, cancellationToken).ConfigureAwait(false);
                }
                await WriteSseAsync(output, new { type = "message", delta = result.AssistantMessage }, cancellationToken).ConfigureAwait(false);
            }
            else if (result.AssistantMessage.Length > emittedMessageLength)
            {
                await WriteSseAsync(output, new
                {
                    type = "message",
                    delta = result.AssistantMessage[emittedMessageLength..],
                }, cancellationToken).ConfigureAwait(false);
            }

            await WriteSseAsync(output, new { type = "done", result }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client disconnected. Do not write to a cancelled response.
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or JsonException
            or InvalidOperationException
            or IOException)
        {
            try
            {
                await WriteSseAsync(output, new { type = "error", message = exception.Message }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The response was already closed by the client.
            }
        }
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
                    formCode,
                    locale,
                    messages,
                    draft.ClinicalSchemaJson,
                    draft.UiSchemaJson,
                    draft.RulesSchemaJson,
                    focus.Fields,
                    focus.Types)),
        ];
    }

    private static ParsedAiOutput ParseModelOutput(
        string raw,
        DraftContext draft,
        string locale)
    {
        JsonObject parsed = ExtractJsonObject(raw)
            ?? throw new ValidationException(
                "AI response was not valid JSON. Ask again or simplify the requirement.");
        string summary = ReadText(parsed["summary"]) ?? "Updated form schemas.";
        string assistantMessage = ReadText(parsed["assistantMessage"]) ?? summary;
        JsonNode? error = parsed["error"];
        if (error is JsonObject errorObject)
        {
            string message = ReadText(errorObject["message"])
                ?? FormAiGuardrails.LimitationMessage(FormAiGuardCode.OutOfScope, locale);
            return ParsedAiOutput.Unchanged(
                FormAiGuardrails.LimitationSummary(locale),
                message,
                draft);
        }

        string mode = ReadText(parsed["mode"])?.ToLowerInvariant() ?? ResolveMode(parsed);
        if (mode == "unchanged")
        {
            return ParsedAiOutput.Unchanged(summary, assistantMessage, draft);
        }

        if (mode == "patch")
        {
            if (parsed["patch"] is not JsonNode patch)
            {
                throw new ValidationException("AI patch response must include a patch object.");
            }

            DraftTriple baseTriple = ParseDraftTriple(draft);
            DraftTriple patched = FormAiDraftPatch.Apply(baseTriple, patch);
            return new ParsedAiOutput(summary, assistantMessage, patched, false);
        }

        if (mode == "replace")
        {
            if (parsed["clinical"] is not JsonObject clinical
                || parsed["ui"] is not JsonObject ui
                || parsed["rules"] is not JsonObject rules)
            {
                throw new ValidationException(
                    "AI replace response must include clinical, ui, and rules objects.");
            }

            return new ParsedAiOutput(
                summary,
                assistantMessage,
                new DraftTriple(
                    (JsonObject)clinical.DeepClone(),
                    (JsonObject)ui.DeepClone(),
                    (JsonObject)rules.DeepClone()),
                false);
        }

        throw new ValidationException(
            "AI response must set mode to unchanged, patch, or replace.");
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
                draft.UiSchemaJson ?? DefaultUiSchema(),
                draft.RulesSchemaJson ?? DefaultRulesSchema());
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
            fallbackUi["layout"] = new JsonArray();
            var fallbackRules = (JsonObject)sanitized.Rules.DeepClone();
            fallbackRules["fields"] = new JsonObject();
            fallbackRules["validations"] = new JsonArray();
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

    private static FocusContext BuildFocusContext(
        FormAiChatRequest request,
        string latestMessage,
        DraftContext draft)
    {
        var ids = new List<string>();
        if (request.FocusedFieldIds is not null)
        {
            ids.AddRange(request.FocusedFieldIds.Where(IsValidFieldId));
        }

        foreach (Match match in FieldMentionRegex().Matches(latestMessage))
        {
            string id = match.Groups["id"].Value;
            if (IsValidFieldId(id))
            {
                ids.Add(id);
            }
        }
        ids = [.. ids.Distinct(StringComparer.Ordinal).Take(MaxFocusedFields)];

        JsonObject clinical = ParseObjectOrEmpty(draft.ClinicalSchemaJson);
        JsonObject ui = ParseObjectOrEmpty(draft.UiSchemaJson);
        JsonObject rules = ParseObjectOrEmpty(draft.RulesSchemaJson);
        var byId = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        IndexFields(clinical["fields"] as JsonArray, byId);
        var fields = new List<FocusedField>();
        foreach (string id in ids)
        {
            if (byId.TryGetValue(id, out JsonNode? field))
            {
                fields.Add(new FocusedField(
                    id,
                    field.DeepClone(),
                    ui["fields"]?[id]?.DeepClone(),
                    rules["fields"]?[id]?.DeepClone()));
            }
        }

        var types = new List<FocusedFieldType>();
        foreach (Match match in TypeMentionRegex().Matches(latestMessage))
        {
            string type = match.Groups["type"].Value.ToUpperInvariant();
            if (type.Length > 0 && types.All(item => item.Type != type))
            {
                types.Add(new FocusedFieldType(type, []));
            }
        }
        if (request.FocusedFieldTypes is not null)
        {
            foreach (string type in request.FocusedFieldTypes.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                string normalized = type.Trim().ToUpperInvariant();
                if (types.All(item => item.Type != normalized))
                {
                    types.Add(new FocusedFieldType(normalized, []));
                }
            }
        }
        return new FocusContext(fields, types);
    }

    private static void IndexFields(JsonArray? fields, Dictionary<string, JsonNode> result)
    {
        if (fields is null)
        {
            return;
        }
        foreach (JsonNode? node in fields)
        {
            if (node is not JsonObject field || field["id"]?.GetValue<string>() is not string id)
            {
                continue;
            }
            result[id] = field;
            IndexFields(field["items"] as JsonArray, result);
        }
    }

    private static async Task WriteSseAsync(
        Stream output,
        object payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] prefix = Encoding.UTF8.GetBytes("data: ");
        byte[] suffix = Encoding.UTF8.GetBytes("\n\n");
        byte[] message = new byte[prefix.Length + json.Length + suffix.Length];
        Buffer.BlockCopy(prefix, 0, message, 0, prefix.Length);
        Buffer.BlockCopy(json, 0, message, prefix.Length, json.Length);
        Buffer.BlockCopy(suffix, 0, message, prefix.Length + json.Length, suffix.Length);
        await output.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static PartialStringField? ExtractPartialJsonStringField(string buffer, string field)
    {
        string key = $"\"{field}\"";
        int keyIndex = buffer.IndexOf(key, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }
        int colon = buffer.IndexOf(':', keyIndex + key.Length);
        if (colon < 0)
        {
            return null;
        }
        int index = colon + 1;
        while (index < buffer.Length && char.IsWhiteSpace(buffer[index]))
        {
            index++;
        }
        if (index >= buffer.Length || buffer[index] != '"')
        {
            return null;
        }

        var value = new StringBuilder();
        bool escaped = false;
        for (index++; index < buffer.Length; index++)
        {
            char character = buffer[index];
            if (escaped)
            {
                _ = value.Append(character switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    _ => character,
                });
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
                return new PartialStringField(value.ToString(), true);
            }
            _ = value.Append(character);
        }
        return new PartialStringField(value.ToString(), false);
    }

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
        return result.Count == 0 || result.All(item => item.Role != "user")
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

        return normalized.StartsWith("EN", StringComparison.Ordinal) ? "en" : normalized[..Math.Min(16, normalized.Length)];
    }

    private static FormAiChatResponse LimitationResponse(
        DraftContext draft,
        string message,
        string locale)
    {
        return new FormAiChatResponse(
            FormAiGuardrails.LimitationSummary(locale),
            message,
            null,
            draft.ClinicalSchemaJson,
            draft.UiSchemaJson ?? DefaultUiSchema(),
            draft.RulesSchemaJson ?? DefaultRulesSchema());
    }

    private static FormAiChatResponse EmptyDraftResponse(string locale)
    {
        DraftTriple empty = FormAiDraftPatch.Empty();
        return new FormAiChatResponse(
            FormAiGuardrails.DraftResetSummary(locale),
            FormAiGuardrails.DraftResetMessage(locale),
            null,
            empty.Clinical.ToJsonString(),
            empty.Ui.ToJsonString(),
            empty.Rules.ToJsonString());
    }

    private static string DefaultUiSchema()
    {
        return /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"layout\":[]}";
    }

    private static string DefaultRulesSchema()
    {
        return /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"validations\":[]}";
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
        if (parsed["patch"] is JsonObject)
        {
            return "patch";
        }

        return parsed["clinical"] is JsonObject
                    && parsed["ui"] is JsonObject
                    && parsed["rules"] is JsonObject
            ? "replace"
            : "unchanged";
    }

    private static bool IsValidFieldId(string value)
    {
        return FieldIdRegex().IsMatch(value.Trim());
    }

    private sealed record DraftContext(
        string ClinicalSchemaJson,
        string? UiSchemaJson,
        string? RulesSchemaJson);

    private sealed record FocusContext(
        IReadOnlyList<FocusedField> Fields,
        IReadOnlyList<FocusedFieldType> Types);

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
                true);
        }
    }

    private sealed record PartialStringField(string Value, bool Complete);

    [GeneratedRegex("@(?<id>[a-zA-Z][\\w-]*)")]
    private static partial Regex FieldMentionRegex();

    [GeneratedRegex("#(?<type>[a-zA-Z][\\w-]*)")]
    private static partial Regex TypeMentionRegex();

    [GeneratedRegex("^[a-zA-Z][\\w-]{0,63}$")]
    private static partial Regex FieldIdRegex();
}
