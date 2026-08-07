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
/// Creates workflow definitions through <see cref="IWorkflowLifecycleService"/>
/// so draft seeding, validation, and audit stay in the application layer.
/// Resource reads enforce tenant scope by raising 404 for cross-tenant
/// identifiers, preventing one hospital from probing another hospital's
/// catalog.
/// </summary>
public sealed class WorkflowDefinitionResourceService(
    IResourceRepositoryAccessor repositoryAccessor,
    IQueryLayerComposer queryLayerComposer,
    IPaginationContext paginationContext,
    IJsonApiOptions options,
    ILoggerFactory loggerFactory,
    IJsonApiRequest request,
    IResourceChangeTracker<WorkflowDefinition> resourceChangeTracker,
    IResourceDefinitionAccessor resourceDefinitionAccessor,
    IWorkflowLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    ICapabilityGuard capabilityGuard,
    ISensitiveReadAuditor sensitiveReadAuditor,
    CynaraDbContext dbContext)
    : JsonApiResourceService<WorkflowDefinition, Guid>(
        repositoryAccessor,
        queryLayerComposer,
        paginationContext,
        options,
        loggerFactory,
        request,
        resourceChangeTracker,
        resourceDefinitionAccessor)
{
    private const string DefaultWorkflowSchemaJson =
        /*lang=json,strict*/ """
            {
              "$schema": "https://cynara.dev/schemas/v1/workflow-schema.schema.json",
              "schemaVersion": "1.0.0",
              "nodes": [
                { "id": "start", "type": "start", "name": "Workflow starts" },
                { "id": "end", "type": "end", "name": "Completed" }
              ],
              "edges": [
                { "from": "start", "to": "end", "label": "Begin" }
              ]
            }
            """;

    public override async Task<WorkflowDefinition?> CreateAsync(
        WorkflowDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        string workflowSchema = string.IsNullOrWhiteSpace(
            resource.InitialWorkflowSchemaJson)
            ? DefaultWorkflowSchemaJson
            : resource.InitialWorkflowSchemaJson;

        WorkflowSummaryDto created = await lifecycle.CreateAsync(
            new CreateWorkflowRequest(
                resource.Code,
                resource.Name,
                workflowSchema),
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        WorkflowDefinition definition = await dbContext.WorkflowDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code
                    && item.HospitalId == hospitalContext.HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<WorkflowDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);

        var ownership = await dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(item => new { item.Id, item.HospitalId, item.DeletedAt })
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (ownership is null
            || ownership.HospitalId != hospitalContext.HospitalId
            || ownership.DeletedAt is not null)
        {
            throw new Application.NotFoundException(
                $"Workflow definition '{id}' was not found.");
        }

        WorkflowDefinition? definition = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (definition is not null
            && httpContextAccessor.HttpContext is { } httpContext
            && HttpMethods.IsGet(httpContext.Request.Method))
        {
            await sensitiveReadAuditor.RecordAsync(
                AuditEntityTypes.WorkflowDefinition,
                definition.Id,
                "workflow.read",
                httpContext.GetActorId(),
                httpContext.Request.Path,
                cancellationToken).ConfigureAwait(false);
        }

        return definition!;
    }

    public override async Task<IReadOnlyCollection<WorkflowDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyCollection<WorkflowDefinition> definitions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. definitions.Where(
            item => item.HospitalId == hospitalContext.HospitalId)];
    }

    public override Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        throw new Application.InvalidStateException(
            "Workflow definitions cannot be hard-deleted via JSON:API. "
            + "Use DELETE /api/workflowDefinitions/{id}/soft-delete-draft.");
    }
}
