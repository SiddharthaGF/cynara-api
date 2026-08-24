using Cynara.Application.Common;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

using JsonApiDotNetCore.Resources;

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
    IWorkflowLifecycleService lifecycle,
    JsonApiResourceDeps deps,
    IResourceChangeTracker<WorkflowDefinition> resourceChangeTracker)
    : TenantScopedResourceService<WorkflowDefinition, Guid>(
        deps,
        resourceChangeTracker)
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
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsWrite,
            cancellationToken).ConfigureAwait(false);

        string workflowSchema = string.IsNullOrWhiteSpace(
            resource.InitialWorkflowSchemaJson)
            ? DefaultWorkflowSchemaJson
            : resource.InitialWorkflowSchemaJson;

        WorkflowSummaryDto created = await lifecycle.CreateAsync(
            new CreateWorkflowRequest(
                resource.Code,
                resource.Name,
                workflowSchema),
            ActorId,
            cancellationToken).ConfigureAwait(false);

        WorkflowDefinition definition = await DbContext.WorkflowDefinitions
            .AsNoTracking()
            .SingleAsync(
                item => item.Code == created.Code
                    && item.HospitalId == HospitalId,
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(definition.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task<WorkflowDefinition> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsRead,
            cancellationToken).ConfigureAwait(false);

        TenantOwnership? ownership = await DbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new TenantOwnership(item.HospitalId, item.DeletedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        EnsureTenantOwnedActive(ownership, id, "Workflow definition");

        WorkflowDefinition? definition = await base.GetAsync(id, cancellationToken)
            .ConfigureAwait(false);

        if (definition is not null)
        {
            await RecordReadAuditAsync(
                definition.Id,
                AuditEntityTypes.WorkflowDefinition,
                "workflow.read",
                cancellationToken).ConfigureAwait(false);
        }

        return definition!;
    }

    public override async Task<IReadOnlyCollection<WorkflowDefinition>> GetAsync(
        CancellationToken cancellationToken)
    {
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsRead,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<WorkflowDefinition> definitions = await base
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. definitions.Where(item => item.HospitalId == HospitalId)];
    }

    public override async Task<WorkflowDefinition?> UpdateAsync(
        Guid id,
        WorkflowDefinition resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);
        await RequireCapabilityAsync(
            CapabilityCodes.WorkflowsWrite,
            cancellationToken).ConfigureAwait(false);

        return await base.UpdateAsync(id, resource, cancellationToken)
            .ConfigureAwait(false);
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
