using Cynara.Application.Components;

namespace Cynara.Application.Modules.Components;

public interface IComponentQueryService
{
    public Task<IReadOnlyList<ComponentSummaryDto>> ListAsync(
        CancellationToken cancellationToken);

    public Task<ComponentSummaryDto> GetSummaryAsync(
        string code,
        CancellationToken cancellationToken);

    public Task<ComponentVersionDto> GetDraftAsync(
        string code,
        CancellationToken cancellationToken);
}
