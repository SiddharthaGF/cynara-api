using Cynara.Application.Components;

namespace Cynara.Application.Modules.Components;

public interface IComponentLifecycleService
{
    public Task<ComponentSummaryDto> CreateAsync(
        CreateComponentRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<ComponentVersionDto> UpdateDraftAsync(
        string code,
        UpdateComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<ComponentVersionDto> PublishDraftAsync(
        string code,
        PublishComponentDraftRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<ComponentVersionDto> CreateDraftFromLatestAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken);

    public Task<ComponentVersionDto> RetireVersionAsync(
        string code,
        string version,
        string? actorId,
        CancellationToken cancellationToken);

    public Task SoftDeleteDraftAsync(
        string code,
        string? actorId,
        CancellationToken cancellationToken);
}
