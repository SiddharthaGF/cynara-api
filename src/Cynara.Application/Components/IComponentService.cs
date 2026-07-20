namespace Cynara.Application.Components;

public interface IComponentService
{
    public Task<ComponentSummaryDto> CreateAsync(CreateComponentRequest request, string? actorId, CancellationToken cancellationToken);

    public Task<IReadOnlyList<ComponentSummaryDto>> ListAsync(CancellationToken cancellationToken);

    public Task<ComponentSummaryDto> GetSummaryAsync(string code, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> GetDraftAsync(string code, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> GetVersionAsync(string code, string version, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> UpdateDraftAsync(string code, UpdateComponentDraftRequest request, string? actorId, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> PublishDraftAsync(string code, PublishComponentDraftRequest request, string? actorId, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> CreateDraftFromLatestAsync(string code, string? actorId, CancellationToken cancellationToken);

    public Task<ComponentVersionDto> RetireVersionAsync(string code, string version, string? actorId, CancellationToken cancellationToken);

    public Task SoftDeleteDraftAsync(string code, string? actorId, CancellationToken cancellationToken);
}
