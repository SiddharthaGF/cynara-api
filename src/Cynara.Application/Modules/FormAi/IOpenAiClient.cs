namespace Cynara.Application.Modules.FormAi;

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

public sealed record OpenAiMessage(string Role, string Content);

public sealed record OpenAiCompletionResult(
    string Content,
    string? Thinking,
    bool IsTruncated = false);

public sealed record OpenAiStreamDelta(
    string? Content,
    string? Reasoning,
    bool IsTruncated = false);
