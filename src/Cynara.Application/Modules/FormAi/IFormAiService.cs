namespace Cynara.Application.Modules.FormAi;

public interface IFormAiService
{
    public Task<FormAiStatusResponse> GetStatusAsync(
        CancellationToken cancellationToken);

    public Task<FormAiChatResponse> ChatAsync(
        string formCode,
        FormAiChatRequest request,
        CancellationToken cancellationToken);

    public Task ChatStreamAsync(
        string formCode,
        FormAiChatRequest request,
        Stream output,
        CancellationToken cancellationToken);
}
