using Cynara.Application.Common;
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
            CapabilityCodes.CatalogRead, cancellationToken)
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
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return WorkflowMappers.ToSummary(definition);
    }

    public async Task<WorkflowVersionDto> GetDraftAsync(
        string code,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        WorkflowVersion draft = WorkflowWorkflowHelpers.RequireDraft(definition);
        return WorkflowMappers.ToVersionDto(definition, draft);
    }

    public async Task<WorkflowVersionDto> GetVersionAsync(
        string code,
        string version,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        SemverRules.EnsureValid(version);
        WorkflowDefinition definition = await WorkflowWorkflowHelpers
            .RequireDefinitionAsync(workflows, code, track: false, hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        WorkflowVersion published = definition.Versions.SingleOrDefault(
                item => string.Equals(item.Version, version, StringComparison.Ordinal)
                    && item.Status != WorkflowVersionStatus.Draft)
            ?? throw new NotFoundException(
                $"Workflow '{code}' version '{version}' was not found.");
        return WorkflowMappers.ToVersionDto(definition, published);
    }
}
