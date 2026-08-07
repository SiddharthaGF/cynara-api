using Cynara.Api.Common.ActorContext;
using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Queries;
using JsonApiDotNetCore.Repositories;
using JsonApiDotNetCore.Resources;
using JsonApiDotNetCore.Services;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Services;

/// <summary>
/// Routes workflow-version writes through lifecycle services and rejects
/// creation/hard-delete over JSON:API, which would bypass the state machine.
/// </summary>
public sealed class WorkflowVersionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<WorkflowVersion> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IWorkflowLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    ICapabilityGuard capabilityGuard,
    ISensitiveReadAuditor sensitiveReadAuditor,
    CynaraDbContext dbContext)
    : JsonApiResourceService<WorkflowVersion, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
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
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkflowsRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.WorkflowVersions
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null || ownership.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");
        }

        WorkflowVersion? version = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (version is not null
            && httpContextAccessor.HttpContext is { } httpContext
            && HttpMethods.IsGet(httpContext.Request.Method))
        {
            await sensitiveReadAuditor.RecordAsync(
                AuditEntityTypes.WorkflowVersion,
                version.Id,
                "workflow.version.read",
                httpContext.GetActorId(),
                httpContext.Request.Path,
                cancellationToken).ConfigureAwait(false);
        }

        return version!;
    }

    public override async Task<IReadOnlyCollection<WorkflowVersion>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkflowsRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<WorkflowVersion> versions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. versions.Where(
            item => item.HospitalId == hospitalContext.HospitalId)];
    }

    public override async Task<WorkflowVersion?> UpdateAsync(
        Guid id,
        WorkflowVersion resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkflowsWrite, cancellationToken)
            .ConfigureAwait(false);

        WorkflowVersion existing = await dbContext.WorkflowVersions
            .Include(item => item.WorkflowDefinition)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");

        if (existing.HospitalId != hospitalContext.HospitalId
            || existing.WorkflowDefinition.HospitalId != hospitalContext.HospitalId)
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
            httpContextAccessor.HttpContext?.GetActorId(),
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
