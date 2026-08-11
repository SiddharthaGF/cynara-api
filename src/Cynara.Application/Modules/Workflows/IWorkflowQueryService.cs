using Cynara.Application.Workflows;

namespace Cynara.Application.Modules.Workflows;

public interface IWorkflowQueryService
{
    public Task<IReadOnlyList<WorkflowSummaryDto>> ListAsync(
        CancellationToken cancellationToken);

    public Task<WorkflowSummaryDto> GetSummaryAsync(
        string code,
        CancellationToken cancellationToken);
}
