using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Capabilities;
using Cynara.Domain.Workflows;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API controller for clinical workflow catalog definitions. Reads
/// require <c>workflows.read</c>; every mutation (create, patch, draft
/// creation, soft-delete) requires <c>workflows.write</c>. The class-level
/// requirement gates the inherited read routes; the write routes declare the
/// stricter requirement explicitly.
/// </summary>
[Route("api/workflowDefinitions")]
[RequireCapability(CapabilityCodes.WorkflowsRead)]
public sealed class WorkflowDefinitionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<WorkflowDefinition, Guid> resourceService,
    IWorkflowLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor)
    : JsonApiController<WorkflowDefinition, Guid>(
        options,
        resourceGraph,
        loggerFactory,
        resourceService)
{
    private readonly IResourceService<WorkflowDefinition, Guid> resourceService =
        resourceService;

    /// <summary>
    /// Creates a draft workflow version from the latest published version.
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the workflow definition does not exist.
    /// </exception>
    [HttpPost("{id}/create-draft", Name = "createWorkflowDraft")]
    [RequireCapability(CapabilityCodes.WorkflowsWrite)]
    [EndpointDescription(
        "Creates a draft workflow version from the latest published version. "
        + "Conflicts when an editable draft already exists.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDraftAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        WorkflowDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Workflow definition '{id}' was not found.");

        if (definition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Workflow definition '{id}' was not found.");
        }

        WorkflowVersionDto draft = await lifecycle.CreateDraftFromLatestAsync(
            definition.Code,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        return Created($"/api/workflowVersions/{draft.Id}", value: null);
    }

    /// <summary>
    /// Soft-deletes a workflow definition that has only drafts
    /// (no published versions).
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the workflow definition does not exist.
    /// </exception>
    [HttpDelete("{id}/soft-delete-draft", Name = "softDeleteWorkflowDraft")]
    [RequireCapability(CapabilityCodes.WorkflowsWrite)]
    [EndpointDescription(
        "Soft-deletes a workflow definition when no published versions remain.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SoftDeleteDraftAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        WorkflowDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Workflow definition '{id}' was not found.");

        if (definition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Workflow definition '{id}' was not found.");
        }

        await lifecycle.SoftDeleteDraftAsync(
            definition.Code,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Creates a workflow definition. Requires <c>workflows.write</c>; the
    /// resource service seeds the initial draft through the lifecycle.
    /// </summary>
    [HttpPost]
    [RequireCapability(CapabilityCodes.WorkflowsWrite)]
    public override Task<IActionResult> PostAsync(
        WorkflowDefinition resource,
        CancellationToken cancellationToken)
    {
        return base.PostAsync(resource, cancellationToken);
    }

    /// <summary>
    /// Updates a workflow definition. Requires <c>workflows.write</c>.
    /// </summary>
    [HttpPatch("{id}")]
    [RequireCapability(CapabilityCodes.WorkflowsWrite)]
    public override Task<IActionResult> PatchAsync(
        Guid id,
        WorkflowDefinition resource,
        CancellationToken cancellationToken)
    {
        return base.PatchAsync(id, resource, cancellationToken);
    }

    /// <summary>
    /// Hard delete is rejected by the resource service; the endpoint still
    /// requires <c>workflows.write</c>.
    /// </summary>
    [HttpDelete("{id}")]
    [RequireCapability(CapabilityCodes.WorkflowsWrite)]
    public override Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return base.DeleteAsync(id, cancellationToken);
    }
}
