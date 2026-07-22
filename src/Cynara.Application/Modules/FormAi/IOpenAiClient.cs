namespace Cynara.Application.Modules.FormAi;

public sealed record OpenAiMessage(string Role, string Content);

public sealed record OpenAiCompletionResult(string Content, string? Thinking);

public sealed record OpenAiStreamDelta(string? Content, string? Reasoning);

public interface IOpenAiClient
{
    public Task<OpenAiCompletionResult> CreateChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        CancellationToken cancellationToken);

    public IAsyncEnumerable<OpenAiStreamDelta> StreamChatCompletionAsync(
        IReadOnlyList<OpenAiMessage> messages,
        OpenAiConfig config,
        CancellationToken cancellationToken);
}
