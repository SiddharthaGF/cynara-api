using System.Text;
using System.Text.Json;
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
    private const string AiModePatch = "patch";
    private const string DefaultUiSchema =
        /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"layout\":[]}";

    private const string DefaultRulesSchema =
        /*lang=json,strict*/ "{\"schemaVersion\":\"1.0.0\",\"clinicalSchemaVersion\":\"1.0.0\",\"fields\":{},\"validations\":[]}";

    [GeneratedRegex(
        "@(?<id>[a-zA-Z][\\w-]*)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldMentionRegex { get; }

    [GeneratedRegex(
        "#(?<type>[a-zA-Z][\\w-]*)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypeMentionRegex { get; }

    [GeneratedRegex(
        "^[a-zA-Z][\\w-]{0,63}$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FieldIdRegex { get; }

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
        EnsureCompleteProviderOutput(completion.IsTruncated);
        ParsedAiOutput parsed = ParseModelOutput(completion.Content, draft, locale);
        FormAiChatResponse result = PrepareResponse(
            parsed,
            draft,
            completion.Thinking,
            out FormAiFallbackReport fallback);
        return FormAiDraftConsistency.EnsureConsistent(
            new FormAiDraftConsistency.EnsureConsistentRequest(
                result,
                draft.ClinicalSchemaJson,
                draft.UiSchemaJson,
                draft.RulesSchemaJson,
                latestUser.Content,
                locale,
                parsed.IsRefusal,
                fallback));
    }

    /// <summary>Streams a chat completion as SSE events.</summary>
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
            if (await TryWriteGuardOrResetResponse(
                    output,
                    draft,
                    latestUser.Content,
                    locale,
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            FocusContext focus = BuildFocusContext(request, latestUser.Content, draft);
            OpenAiConfig config = await settings.ResolveActiveConfigAsync(cancellationToken).ConfigureAwait(false);
            StreamPartialState partialState = await ConsumeStreamAndEmitPartials(
                output,
                BuildMessages(formCode, locale, messages, draft, focus),
                config,
                formCode,
                cancellationToken).ConfigureAwait(false);
            EnsureCompleteProviderOutput(partialState.IsTruncated);

            OpenAiCompletionResult completion = new(
                partialState.RawContent.ToString(),
                partialState.Thinking.Length == 0 ? null : partialState.Thinking.ToString(),
                partialState.IsTruncated);
            ParsedAiOutput parsed = ParseModelOutput(completion.Content, draft, locale);
            FormAiChatResponse result = PrepareResponse(
                parsed,
                draft,
                completion.Thinking,
                out FormAiFallbackReport fallback);
            result = FormAiDraftConsistency.EnsureConsistent(
                new FormAiDraftConsistency.EnsureConsistentRequest(
                    result,
                    draft.ClinicalSchemaJson,
                    draft.UiSchemaJson,
                    draft.RulesSchemaJson,
                    latestUser.Content,
                    locale,
                    parsed.IsRefusal,
                    fallback));
            await EmitFinalMessageTail(
                output,
                result,
                partialState.EmittedMessageLength,
                partialState.MessagePhaseSent,
                cancellationToken).ConfigureAwait(false);
            object payload = fallback.DiscardedSomething
                ? new
                {
                    type = "done",
                    result,
                    notes = new
                    {
                        fallback = new
                        {
                            outcome = fallback.Outcome.ToString(),
                            droppedLayers = fallback.DroppedLayers,
                        },
                    },
                }
                : new { type = "done", result };
            await WriteSseAsync(output, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is HttpRequestException
            or JsonException
            or InvalidOperationException
            or IOException
            or ValidationException)
        {
            await TryWriteStreamError(output, exception).ConfigureAwait(false);
        }
    }

    private static async Task<bool> TryWriteGuardOrResetResponse(
        Stream output,
        DraftContext draft,
        string latestUserContent,
        string locale,
        CancellationToken cancellationToken)
    {
        FormAiGuardViolation? guard = FormAiGuardrails.Detect(latestUserContent, locale);
        if (guard is not null)
        {
            FormAiChatResponse limited = LimitationResponse(draft, guard.Message, locale);
            await WriteSseAsync(
                output,
                new { type = SchemaJsonKeys.Message, delta = limited.AssistantMessage },
                cancellationToken).ConfigureAwait(false);
            await WriteSseAsync(
                output,
                new { type = "done", result = limited },
                cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!FormAiGuardrails.IsDraftReset(latestUserContent))
        {
            return false;
        }

        FormAiChatResponse empty = EmptyDraftResponse(locale);
        await WriteSseAsync(
            output,
            new { type = SchemaJsonKeys.Message, delta = empty.AssistantMessage },
            cancellationToken).ConfigureAwait(false);
        await WriteSseAsync(
            output,
            new { type = "done", result = empty },
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<StreamPartialState> ConsumeStreamAndEmitPartials(
        Stream output,
        IReadOnlyList<OpenAiMessage> chatMessages,
        OpenAiConfig config,
        string formCode,
        CancellationToken cancellationToken)
    {
        var rawContent = new StringBuilder();
        var thinking = new StringBuilder();
        int emittedMessageLength = 0;
        bool messagePhaseSent = false;
        bool schemaPhaseSent = false;
        bool isTruncated = false;

        await foreach (OpenAiStreamDelta delta in openAi.StreamChatCompletionAsync(
                           chatMessages,
                           config,
                           formCode,
                           cancellationToken).ConfigureAwait(false))
        {
            _ = rawContent.Append(delta.Content ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(delta.Reasoning))
            {
                _ = thinking.Append(delta.Reasoning);
            }

            isTruncated |= delta.IsTruncated;

            if (rawContent.Length == 0)
            {
                continue;
            }

            PartialStringField? partial = ExtractPartialJsonStringField(
                rawContent.ToString(),
                "assistantMessage");
            if (partial is null)
            {
                continue;
            }

            if (!messagePhaseSent)
            {
                messagePhaseSent = true;
                await WriteSseAsync(
                    output,
                    new { type = "phase", phase = SchemaJsonKeys.Message },
                    cancellationToken).ConfigureAwait(false);
            }

            if (partial.Value.Length > emittedMessageLength)
            {
                string chunk = partial.Value[emittedMessageLength..];
                emittedMessageLength = partial.Value.Length;
                await WriteSseAsync(
                    output,
                    new { type = SchemaJsonKeys.Message, delta = chunk },
                    cancellationToken).ConfigureAwait(false);
            }

            if (partial.Complete && !schemaPhaseSent)
            {
                schemaPhaseSent = true;
                await WriteSseAsync(
                    output,
                    new { type = "phase", phase = "schema" },
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return new StreamPartialState(
            rawContent,
            thinking,
            emittedMessageLength,
            messagePhaseSent,
            IsTruncated: isTruncated);
    }

    private static void EnsureCompleteProviderOutput(bool isTruncated)
    {
        if (isTruncated)
        {
            throw new ValidationException(
                "AI provider truncated the response before completing the JSON output. Increase the output budget or try again.");
        }
    }

    private static async Task EmitFinalMessageTail(
        Stream output,
        FormAiChatResponse result,
        int emittedMessageLength,
        bool messagePhaseSent,
        CancellationToken cancellationToken)
    {
        if (emittedMessageLength == 0 && result.AssistantMessage.Length > 0)
        {
            if (!messagePhaseSent)
            {
                await WriteSseAsync(
                    output,
                    new { type = "phase", phase = SchemaJsonKeys.Message },
                    cancellationToken).ConfigureAwait(false);
            }

            await WriteSseAsync(
                output,
                new { type = SchemaJsonKeys.Message, delta = result.AssistantMessage },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.AssistantMessage.Length <= emittedMessageLength)
        {
            return;
        }

        await WriteSseAsync(
            output,
            new
            {
                type = SchemaJsonKeys.Message,
                delta = result.AssistantMessage[emittedMessageLength..],
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes one SSE error event; swallows <see cref="IOException"/> when
    /// the client has already closed the response stream.
    /// </summary>
    private static async Task TryWriteStreamError(Stream output, Exception exception)
    {
        try
        {
            await WriteSseAsync(
                output,
                new { type = "error", message = exception.Message },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
    }
}
