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

    private enum ChunkStatus
    {
        Chunk = 0,
        EndOfStream = 1,
        FirstChunkTimedOut = 2,
        FirstChunkFailed = 3,
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

    internal static async IAsyncEnumerable<OpenAiStreamDelta> EnumerateWithFirstChunkRetryAsync(
        Func<IChatClient> clientFactory,
        Func<List<ChatMessage>> messagesFactory,
        Func<ChatOptions> optionsFactory,
        OpenAiConfig config,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        bool retried = false;
        while (true)
        {
            AttemptOutcome outcome = await TryStreamOnceAsync(
                clientFactory,
                messagesFactory,
                optionsFactory,
                config,
                cancellationToken).ConfigureAwait(false);
            foreach (OpenAiStreamDelta delta in outcome.Deltas)
            {
                yield return delta;
            }

            if (outcome.ExhaustedStream || outcome.FirstChunkFailure is null)
            {
                yield break;
            }

            if (retried)
            {
                throw MapProviderException(outcome.FirstChunkFailure);
            }

            retried = true;
            if (!outcome.EmittedAny && outcome.ExhaustedStream)
            {
                // The provider produced no chunks but the stream signalled an
                // end-of-stream without raising; treat it as a fatal failure so
                // we don't loop pointlessly on a healthy-looking empty stream.
                throw new ValidationException(
                    "OpenAI-compatible provider returned an empty assistant message.");
            }
        }

        // Unreachable: the loop above always either yields, throws, or breaks.
        throw new InvalidOperationException("Unreachable");
    }

    private static async Task<AttemptOutcome> TryStreamOnceAsync(
        Func<IChatClient> clientFactory,
        Func<List<ChatMessage>> messagesFactory,
        Func<ChatOptions> optionsFactory,
        OpenAiConfig config,
        CancellationToken cancellationToken)
    {
        using IChatClient chat = clientFactory();
        List<ChatMessage> chatMessages = messagesFactory();
        ChatOptions options = optionsFactory();
        IAsyncEnumerator<ChatResponseUpdate> inner = chat
            .GetStreamingResponseAsync(chatMessages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var deltas = new List<OpenAiStreamDelta>();
        try
        {
            bool firstChunkConsumed = false;
            Exception? firstChunkFailure = null;
            bool exhaustedStream = false;
            bool emittedAny = false;
            bool keepReading = true;
            while (keepReading)
            {
                ChunkResult chunk = await ReadNextChunkAsync(
                    inner,
                    firstChunkConsumed,
                    config.FirstChunkTimeout,
                    cancellationToken).ConfigureAwait(false);
                switch (chunk.Status)
                {
                    case ChunkStatus.FirstChunkTimedOut:
                        firstChunkFailure = new TimeoutException(
                            FormatFirstChunkTimeout(config.FirstChunkTimeout));
                        keepReading = false;
                        break;
                    case ChunkStatus.FirstChunkFailed:
                        firstChunkFailure = chunk.Exception;
                        keepReading = false;
                        break;
                    case ChunkStatus.EndOfStream:
                        exhaustedStream = true;
                        keepReading = false;
                        break;
                    case ChunkStatus.Chunk:
                        firstChunkConsumed = true;
                        OpenAiStreamDelta? delta = ToStreamDelta(inner.Current);
                        if (delta is null)
                        {
                            continue;
                        }

                        emittedAny = true;
                        deltas.Add(delta);
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unhandled {nameof(ChunkStatus)} value: {chunk.Status}");
                }
            }

            return new AttemptOutcome(deltas, firstChunkFailure, exhaustedStream, emittedAny);
        }
        finally
        {
            await DisposeQuietlyAsync(inner).ConfigureAwait(false);
        }
    }

    private static async Task<ChunkResult> ReadNextChunkAsync(
        IAsyncEnumerator<ChatResponseUpdate> inner,
        bool firstChunkConsumed,
        TimeSpan firstChunkTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (firstChunkConsumed)
            {
                bool moved = await inner.MoveNextAsync().ConfigureAwait(false);
                return moved
                    ? new ChunkResult(Status: ChunkStatus.Chunk, Exception: null)
                    : new ChunkResult(Status: ChunkStatus.EndOfStream, Exception: null);
            }

            bool firstMoved = await WaitForFirstChunkAsync(
                enumerator: inner,
                timeout: firstChunkTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return firstMoved
                ? new ChunkResult(Status: ChunkStatus.Chunk, Exception: null)
                : new ChunkResult(Status: ChunkStatus.FirstChunkTimedOut, Exception: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ValidationException and not OperationCanceledException)
        {
            if (firstChunkConsumed)
            {
                throw MapProviderException(ex);
            }

            return new ChunkResult(ChunkStatus.FirstChunkFailed, ex);
        }
    }

    private static async Task DisposeQuietlyAsync(IAsyncEnumerator<ChatResponseUpdate> inner)
    {
        ValueTask dispose = inner.DisposeAsync();
        if (dispose.IsCompletedSuccessfully)
        {
            return;
        }

        try
        {
            await dispose.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposal raced with a cancellation; nothing to do.
        }
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

    private sealed record ChunkResult(ChunkStatus Status, Exception? Exception);

    private sealed record AttemptOutcome(
        IReadOnlyList<OpenAiStreamDelta> Deltas,
        Exception? FirstChunkFailure,
        bool ExhaustedStream,
        bool EmittedAny);
}
