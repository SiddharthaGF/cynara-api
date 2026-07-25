using Cynara.Application;
using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.AI;

using Polly;
using Polly.Registry;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed partial class OpenAiClient(
    IOpenAiChatClientFactory chatClientFactory,
    ResiliencePipelineProvider<string> pipelineProvider) : IOpenAiClient
{
    internal const string ResiliencePipelineKey = "openai";

    private readonly IOpenAiChatClientFactory chatClientFactory =
        chatClientFactory
        ?? throw new ArgumentNullException(nameof(chatClientFactory));

    private readonly ResiliencePipeline resilience =
        (pipelineProvider
            ?? throw new ArgumentNullException(nameof(pipelineProvider)))
        .GetPipeline(ResiliencePipelineKey);

    private enum ChunkStatus
    {
        None = 0,
        Chunk = 1,
        EndOfStream = 2,
        FirstChunkTimedOut = 3,
        FirstChunkFailed = 4,
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
        return EnumerateWithFirstChunkRetryAsync(
            () => chatClientFactory.Create(config),
            () => ToChatMessages(messages, config, cacheScope),
            () => CreateChatOptions(config, cacheScope),
            config,
            cancellationToken);
    }

    private static ValidationException MapProviderException(Exception exception)
    {
        return OpenAiProviderErrorMapper.Map(exception);
    }
}
