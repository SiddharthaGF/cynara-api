namespace Cynara.Application.Modules.FormAi;

public sealed record OpenAiMessage(string Role, string Content)
{
    /// <summary>
    /// Anthropic-style cache breakpoint attached to a single message.
    /// Only honored by providers detected as supporting it; ignored otherwise.
    /// </summary>
    public IReadOnlyDictionary<string, string>? CacheControl { get; init; }
}

public sealed record OpenAiCompletionResult(string Content, string? Thinking);

public sealed record OpenAiStreamDelta(string? Content, string? Reasoning);

public interface IOpenAiClient
{
    public Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken);

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        string? cacheScope,
        CancellationToken cancellationToken);
}
