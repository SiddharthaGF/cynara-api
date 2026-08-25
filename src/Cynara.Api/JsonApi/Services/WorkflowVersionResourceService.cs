using Cynara.Application.Common;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

using JsonApiDotNetCore.Resources;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Routes workflow-version writes through lifecycle services and rejects
/// creation/hard-delete over JSON:API, which would bypass the state machine.
/// </summary>
public sealed class WorkflowVersionResourceService(
    IWorkflowLifecycleService lifecycle,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<WorkflowVersion> resourceChangeTracker)
    : TenantScopedResourceService<WorkflowVersion, Guid>(
        deps,
        resourceChangeTracker)
{
    public override Task<WorkflowVersion?> CreateAsync(
        WorkflowVersion resource,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Create workflow drafts via POST "
            + "/api/workflowDefinitions/{id}/create-draft or by creating a "
            + "workflow definition.");
    }

    public override async Task<WorkflowVersion> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.WorkflowVersions
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwned(ownership, id, "Workflow version");

        WorkflowVersion? version = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (version is not null)
        {
            await RecordReadAuditAsync(
                version.Id,
                AuditEntityTypes.WorkflowVersion,
                "workflow.version.read",
                cancellationToken).ConfigureAwait(false);
        }

        return version!;
    }

    public override async Task<IReadOnlyCollection<WorkflowVersion>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsRead,
            cancellationToken).ConfigureAwait(false);

        return await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<WorkflowVersion?> UpdateAsync(
        Guid id,
        WorkflowVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsWrite,
            cancellationToken).ConfigureAwait(false);

        WorkflowVersion existing = await DbContext.WorkflowVersions
            .Include(item => item.WorkflowDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");

        if (existing.HospitalId != HospitalId
            || existing.WorkflowDefinition.HospitalId != HospitalId)
        {
            throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");
        }

        if (existing.Status != WorkflowVersionStatus.Draft)
        {
            throw new Application.InvalidStateException(
                "Only draft workflow versions can be patched via JSON:API.");
        }

        WorkflowVersionDto updated = await lifecycle.UpdateDraftAsync(
            existing.WorkflowDefinition.Code,
            new UpdateWorkflowDraftRequest(
                resource.WorkflowSchemaJson,
                resource.RowVersion),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        return await GetAsync(updated.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Workflow versions cannot be hard-deleted. Soft-delete the "
            + "definition or retire published versions.");
    }
}
