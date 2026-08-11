using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows.Persistence;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

namespace Cynara.Application.Modules.Workflows;

public sealed class WorkflowQueriesService(
    IWorkflowRepository workflows,
    IHospitalContext hospitalContext,
    ICapabilityGuard capabilityGuard) : IWorkflowQueryService
{
    public async Task<IReadOnlyList<WorkflowSummaryDto>> ListAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkflowsRead, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<WorkflowDefinition> items = await workflows
            .ListDefinitionsAsync(hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return [.. items.Select(WorkflowMappers.ToSummary)];
    }

    public async Task<WorkflowSummaryDto> GetSummaryAsync(
        string code,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkflowsRead, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return WorkflowMappers.ToSummary(definition);
    }
}
