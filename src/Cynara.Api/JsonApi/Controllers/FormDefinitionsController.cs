using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API resource controller for form catalog definitions.
/// Creation seeds an initial draft version via application services.
/// </summary>
[Route("api/formDefinitions")]
public sealed class FormDefinitionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<FormDefinition, Guid> resourceService,
    IFormService formService,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor) : JsonApiController<FormDefinition, Guid>(options, resourceGraph, loggerFactory, resourceService)
{
    private readonly IResourceService<FormDefinition, Guid> resourceService =
        resourceService;

    /// <summary>
    /// Soft-deletes a form definition that has only editable drafts
    /// (no published versions). Emits audit event form.draft.deleted.
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the form definition does not exist.
    /// </exception>
    [HttpDelete("{id}/soft-delete-draft")]
    [EndpointDescription(
        "Soft-deletes a form definition when no published versions remain. "
        + "Requires the current editable draft/review version.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SoftDeleteDraftAsync(
        Guid id,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");

        if (definition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");
        }

        await formService.SoftDeleteDraftAsync(
            definition.Code,
            reason,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    /// <summary>
    /// Creates a new draft version from the latest published version
    /// (or a placeholder schema when none exists).
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the form definition does not exist.
    /// </exception>
    [HttpPost("{id}/create-draft")]
    [EndpointDescription(
        "Creates a draft form version from the latest published version "
        + "for the definition. Conflicts when an editable version already exists.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDraftAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        FormDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");

        if (definition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form definition '{id}' was not found.");
        }

        FormVersionDto draft = await formService.CreateDraftFromLatestAsync(
            definition.Code,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);

        return Created($"/api/formVersions/{draft.Id}", value: null);
    }
}
