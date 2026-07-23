using System.ClientModel.Primitives;

namespace Cynara.Infrastructure.Modules.FormAi;

internal sealed class OpenAiOpenRouterHeadersPolicy : PipelinePolicy
{
    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(message);
        ApplyHeaders(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        ArgumentNullException.ThrowIfNull(message);
        ApplyHeaders(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void ApplyHeaders(PipelineMessage message)
    {
        message.Request.Headers.Set("HTTP-Referer", "https://cynara.app");
        message.Request.Headers.Set("X-Title", "Cynara");
    }
}
