using System.ClientModel;
using System.Runtime.CompilerServices;

using Cynara.Application;
using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.AI;

using Polly;
using Polly.Registry;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed partial class OpenAiClient : IOpenAiClient
{
    internal const string ResiliencePipelineKey = "openai";

    private readonly IOpenAiChatClientFactory chatClientFactory;
    private readonly ResiliencePipeline resilience;

    public OpenAiClient(
        IOpenAiChatClientFactory chatClientFactory,
        ResiliencePipelineProvider<string> pipelineProvider)
    {
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(pipelineProvider);
        this.chatClientFactory = chatClientFactory;
        resilience = pipelineProvider.GetPipeline(ResiliencePipelineKey);
    }

    public async Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);
        EnsureConfigured(config);

        try
        {
            return await resilience.ExecuteAsync(
                async token =>
                {
                    using IChatClient chat = chatClientFactory.Create(config);
                    List<ChatMessage> chatMessages = ToChatMessages(
                        messages,
                        config,
                        cacheScope);
                    ChatOptions options = CreateChatOptions(config, cacheScope);
                    ChatResponse response = await chat.GetResponseAsync(
                        chatMessages,
                        options,
                        token).ConfigureAwait(false);
                    return ToCompletionResult(response);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not ValidationException and not OperationCanceledException)
        {
            throw MapProviderException(ex);
        }
    }

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(config);
        EnsureConfigured(config);
        return EnumerateStreamAsync(
            chatClientFactory.Create(config),
            ToChatMessages(messages, config, cacheScope),
            CreateChatOptions(config, cacheScope),
            cancellationToken);
    }

    private static async IAsyncEnumerable<OpenAiStreamDelta> EnumerateStreamAsync(
        IChatClient chat,
        List<ChatMessage> messages,
        ChatOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (chat)
        {
            bool emittedAny = false;
            IAsyncEnumerator<ChatResponseUpdate> enumerator = chat
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            await using (enumerator.ConfigureAwait(continueOnCapturedContext: false))
            {
                while (true)
                {
                    bool moved;
                    try
                    {
                        moved = await enumerator.MoveNextAsync()
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (
                        ex is not ValidationException
                        and not OperationCanceledException)
                    {
                        throw MapProviderException(ex);
                    }

                    if (!moved)
                    {
                        break;
                    }

                    OpenAiStreamDelta? delta = ToStreamDelta(enumerator.Current);
                    if (delta is null)
                    {
                        continue;
                    }

                    emittedAny = true;
                    yield return delta;
                }
            }

            if (!emittedAny)
            {
                throw new ValidationException(
                    "OpenAI-compatible provider returned an empty assistant message.");
            }
        }
    }

    private static ValidationException MapProviderException(Exception exception)
    {
        return exception switch
        {
            ClientResultException clientEx => new ValidationException(
                string.IsNullOrWhiteSpace(clientEx.Message)
                    ? string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"OpenAI-compatible request failed with HTTP {clientEx.Status}.")
                    : clientEx.Message.Trim(),
                clientEx),
            HttpRequestException httpEx => new ValidationException(
                httpEx.Message,
                httpEx),
            _ => new ValidationException(
                "OpenAI-compatible provider request failed.",
                exception),
        };
    }
}
