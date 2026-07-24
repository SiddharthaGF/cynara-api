using System.Globalization;
using System.Runtime.CompilerServices;

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

    internal static async IAsyncEnumerable<OpenAiStreamDelta> EnumerateWithFirstChunkRetryAsync(
        Func<IChatClient> clientFactory,
        Func<List<ChatMessage>> messagesFactory,
        Func<ChatOptions> optionsFactory,
        OpenAiConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int attempt = 0;
        bool retried = false;
        while (true)
        {
            attempt++;
            using IChatClient chat = clientFactory();
            List<ChatMessage> chatMessages = messagesFactory();
            ChatOptions options = optionsFactory();
            IAsyncEnumerator<ChatResponseUpdate>? inner = null;
            Exception? firstChunkFailure = null;
            bool exhaustedStream = false;
            bool firstChunkConsumed = false;
            bool emittedAny = false;
            try
            {
                inner = chat.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                while (true)
                {
                    bool moved;
                    try
                    {
                        if (!firstChunkConsumed)
                        {
                            moved = await WaitForFirstChunkAsync(
                                inner,
                                config.FirstChunkTimeout,
                                cancellationToken).ConfigureAwait(false);
                            if (!moved)
                            {
                                firstChunkFailure = new TimeoutException(
                                    FormatFirstChunkTimeout(config.FirstChunkTimeout));
                                break;
                            }
                        }
                        else
                        {
                            moved = await inner.MoveNextAsync()
                                .ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) when (
                        ex is not ValidationException
                        and not OperationCanceledException)
                    {
                        if (!firstChunkConsumed)
                        {
                            firstChunkFailure = ex;
                            break;
                        }

                        throw MapProviderException(ex);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    if (!moved)
                    {
                        exhaustedStream = true;
                        break;
                    }

                    firstChunkConsumed = true;
                    OpenAiStreamDelta? delta = ToStreamDelta(inner.Current);
                    if (delta is null)
                    {
                        continue;
                    }

                    emittedAny = true;
                    yield return delta;
                }
            }
            finally
            {
                if (inner is not null)
                {
                    ValueTask dispose = inner.DisposeAsync();
                    if (!dispose.IsCompletedSuccessfully)
                    {
                        try
                        {
                            await dispose.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }
                }
            }

            if (exhaustedStream)
            {
                yield break;
            }

            if (firstChunkFailure is null)
            {
                yield break;
            }

            if (retried)
            {
                throw MapProviderException(firstChunkFailure);
            }

            retried = true;
            if (!emittedAny && exhaustedStream)
            {
                // The provider produced no chunks but the stream signalled an
                // end-of-stream without raising; treat it as a fatal failure so
                // we don't loop pointlessly on a healthy-looking empty stream.
                throw new ValidationException(
                    "OpenAI-compatible provider returned an empty assistant message.");
            }
        }

        // Unreachable: the loop above always either yields, throws, or breaks.
#pragma warning disable CS0162
        throw new InvalidOperationException("Unreachable");
#pragma warning restore CS0162
    }

    private static async Task<bool> WaitForFirstChunkAsync(
        IAsyncEnumerator<ChatResponseUpdate> enumerator,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return await enumerator.MoveNextAsync().ConfigureAwait(false);
        }

        Task<bool> moveNextTask = enumerator.MoveNextAsync().AsTask();
        var delayTask = Task.Delay(timeout, cancellationToken);
        Task firstToComplete = await Task.WhenAny(moveNextTask, delayTask)
            .ConfigureAwait(false);
        if (firstToComplete == moveNextTask)
        {
            return await moveNextTask.ConfigureAwait(false);
        }

        // Timeout fired before the provider produced any chunk. We deliberately
        // do not await moveNextTask here; the outer loop's `finally` will dispose
        // the enumerator, which in turn cancels the inner stream's CTS and lets
        // the hanging MoveNextAsync resolve cleanly instead of leaking an
        // unobserved task exception.
        return false;
    }

    private static ValidationException MapProviderException(Exception exception)
    {
        return OpenAiProviderErrorMapper.Map(exception);
    }

    private static string FormatFirstChunkTimeout(TimeSpan timeout)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AI provider did not deliver the first chunk within {timeout.TotalSeconds:0}s.");
    }
}
