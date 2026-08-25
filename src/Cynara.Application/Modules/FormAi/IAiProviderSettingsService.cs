namespace Cynara.Application.Modules.FormAi;

public interface IAiProviderSettingsService
{
    public Task<OpenAiConfig> ResolveActiveConfigAsync(
        CancellationToken cancellationToken);

    public Task<FormAiSettingsResponse> GetPublicViewAsync(
        CancellationToken cancellationToken);

    public Task<FormAiSettingsResponse> UpsertAsync(
        FormAiSettingsUpdateRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
