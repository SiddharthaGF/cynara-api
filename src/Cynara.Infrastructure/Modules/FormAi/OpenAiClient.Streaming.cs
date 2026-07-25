using System.Globalization;
using System.Runtime.CompilerServices;

using Cynara.Application;
using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.AI;

namespace Cynara.Infrastructure.Modules.FormAi;

public sealed partial class OpenAiClient
{
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

            if (!ShouldRetryAfterFirstChunkFailure(outcome))
            {
                yield break;
            }

            if (retried)
            {
                throw MapProviderException(outcome.FirstChunkFailure!);
            }

            retried = true;
            ThrowOnEmptyStreamIfFatal(outcome);
        }

        // Unreachable: the loop above always either yields, throws, or breaks.
        throw new InvalidOperationException("Unreachable");
    }

    private static bool ShouldRetryAfterFirstChunkFailure(AttemptOutcome outcome)
    {
        return !outcome.ExhaustedStream && outcome.FirstChunkFailure is not null;
    }

    private static void ThrowOnEmptyStreamIfFatal(AttemptOutcome outcome)
    {
        // The provider produced no chunks but the stream signalled an
        // end-of-stream without raising; treat it as a fatal failure so
        // we don't loop pointlessly on a healthy-looking empty stream.
        if (!outcome.EmittedAny && outcome.ExhaustedStream)
        {
            throw new ValidationException(
                "OpenAI-compatible provider returned an empty assistant message.");
        }
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

        try
        {
            StreamAccumulator accumulator = new(config);
            while (accumulator.KeepReading)
            {
                ChunkResult chunk = await ReadNextChunkAsync(
                    inner,
                    accumulator.FirstChunkConsumed,
                    config.FirstChunkTimeout,
                    cancellationToken).ConfigureAwait(false);
                OpenAiStreamDelta? delta = chunk.Status == ChunkStatus.Chunk
                    ? ToStreamDelta(inner.Current)
                    : null;
                accumulator.Apply(chunk, delta);
            }

            return accumulator.ToOutcome();
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
                return await ReadSubsequentChunkAsync(inner).ConfigureAwait(false);
            }

            return await ReadFirstChunkAsync(inner, firstChunkTimeout, cancellationToken)
                .ConfigureAwait(false);
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

    private static async Task<ChunkResult> ReadSubsequentChunkAsync(
        IAsyncEnumerator<ChatResponseUpdate> inner)
    {
        bool moved = await inner.MoveNextAsync().ConfigureAwait(false);
        return moved
            ? new ChunkResult(Status: ChunkStatus.Chunk, Exception: null)
            : new ChunkResult(Status: ChunkStatus.EndOfStream, Exception: null);
    }

    private static async Task<ChunkResult> ReadFirstChunkAsync(
        IAsyncEnumerator<ChatResponseUpdate> inner,
        TimeSpan firstChunkTimeout,
        CancellationToken cancellationToken)
    {
        bool firstMoved = await WaitForFirstChunkAsync(
            enumerator: inner,
            timeout: firstChunkTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return firstMoved
            ? new ChunkResult(Status: ChunkStatus.Chunk, Exception: null)
            : new ChunkResult(Status: ChunkStatus.FirstChunkTimedOut, Exception: null);
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

    private static string FormatFirstChunkTimeout(TimeSpan timeout)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"AI provider did not deliver the first chunk within {timeout.TotalSeconds:0}s.");
    }

    private sealed class StreamAccumulator(OpenAiConfig config)
    {
        private readonly List<OpenAiStreamDelta> deltas = [];
        private Exception? firstChunkFailure;

        public bool FirstChunkConsumed { get; private set; }

        public bool ExhaustedStream { get; private set; }

        public bool EmittedAny { get; private set; }

        public bool KeepReading { get; private set; } = true;

        public void Apply(ChunkResult chunk, OpenAiStreamDelta? delta)
        {
            ChunkStatus status = chunk.Status;
#pragma warning disable IDE0010 // Add missing cases
            switch (status)
            {
                case ChunkStatus.Chunk:
                    FirstChunkConsumed = true;
                    if (delta is null)
                    {
                        return;
                    }

                    EmittedAny = true;
                    deltas.Add(delta);
                    break;
                case ChunkStatus.EndOfStream:
                    ExhaustedStream = true;
                    KeepReading = false;
                    break;
                case ChunkStatus.FirstChunkTimedOut:
                    firstChunkFailure = new TimeoutException(
                        FormatFirstChunkTimeout(config.FirstChunkTimeout));
                    KeepReading = false;
                    break;
                case ChunkStatus.FirstChunkFailed:
                    firstChunkFailure = chunk.Exception;
                    KeepReading = false;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unhandled {nameof(ChunkStatus)} value: {status}");
            }
#pragma warning restore IDE0010 // Add missing cases
        }

        public AttemptOutcome ToOutcome()
        {
            return new AttemptOutcome(deltas, firstChunkFailure, ExhaustedStream, EmittedAny);
        }
    }

    private sealed record ChunkResult(ChunkStatus Status, Exception? Exception);

    private sealed record AttemptOutcome(
        IReadOnlyList<OpenAiStreamDelta> Deltas,
        Exception? FirstChunkFailure,
        bool ExhaustedStream,
        bool EmittedAny);
}
