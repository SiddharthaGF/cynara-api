using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Components;
using Cynara.Domain.Components;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>JSON:API controller for reusable clinical components.</summary>
[Route("api/componentDefinitions")]
public sealed class ComponentDefinitionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<ComponentDefinition, Guid> resourceService,
    IComponentLifecycleService lifecycle,
    IHttpContextAccessor httpContextAccessor)
    : JsonApiController<ComponentDefinition, Guid>(
        options,
        resourceGraph,
        loggerFactory,
        resourceService)
{
    private readonly IResourceService<ComponentDefinition, Guid> resourceService =
        resourceService;

    /// <summary>
    /// Creates a draft component version from the latest published version.
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the component definition does not exist.
    /// </exception>
    [HttpPost("{id}/create-draft", Name = "createComponentDraft")]
    [EndpointDescription(
        "Creates a draft component version from the latest published version. "
        + "Conflicts when an editable draft already exists.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateDraftAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Component definition '{id}' was not found.");

        Application.Components.ComponentVersionDto draft =
            await lifecycle.CreateDraftFromLatestAsync(
                definition.Code,
                httpContextAccessor.HttpContext?.GetActorId(),
                cancellationToken).ConfigureAwait(false);

        return Created($"/api/componentVersions/{draft.Id}", value: null);
    }

    /// <summary>
    /// Soft-deletes a component definition that has only drafts
    /// (no published versions).
    /// </summary>
    /// <exception cref="Application.NotFoundException">
    /// Thrown when the component definition does not exist.
    /// </exception>
    [HttpDelete("{id}/soft-delete-draft", Name = "softDeleteComponentDraft")]
    [EndpointDescription(
        "Soft-deletes a component definition when no published versions remain.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SoftDeleteDraftAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        ComponentDefinition definition = await resourceService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Component definition '{id}' was not found.");

        await lifecycle.SoftDeleteDraftAsync(
            definition.Code,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}

/// <summary>JSON:API controller for component versions and publish/retire.</summary>
[Route("api/componentVersions")]
public sealed class ComponentVersionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<ComponentVersion, Guid> resourceService,
    IComponentLifecycleService lifecycle,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext)
    : JsonApiController<ComponentVersion, Guid>(
        options,
        resourceGraph,
        loggerFactory,
        resourceService)
{
    /// <summary>Publishes a draft component version with the next semver.</summary>
    [HttpPost("{id}/publish", Name = "publishComponentVersion")]
    [EndpointDescription(
        "Publishes a draft component version. Pass rowVersion as a query "
        + "parameter for optimistic concurrency. No request body.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        ComponentVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        Application.Components.ComponentVersionDto published =
            await lifecycle.PublishDraftAsync(
                version.ComponentDefinition.Code,
                new Application.Components.PublishComponentDraftRequest(
                    rowVersion),
                httpContextAccessor.HttpContext?.GetActorId(),
                cancellationToken).ConfigureAwait(false);
        return await GetAsync(published.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Retires a published component version.</summary>
    /// <exception cref="Application.InvalidStateException">
    /// Thrown when the version is not published.
    /// </exception>
    [HttpPost("{id}/retire", Name = "retireComponentVersion")]
    [EndpointDescription(
        "Retires a published component version. Retired versions remain readable.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        ComponentVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version.Version))
        {
            throw new Application.InvalidStateException(
                "Only published component versions can be retired.");
        }

        Application.Components.ComponentVersionDto retired =
            await lifecycle.RetireVersionAsync(
                version.ComponentDefinition.Code,
                version.Version,
                httpContextAccessor.HttpContext?.GetActorId(),
                cancellationToken).ConfigureAwait(false);
        return await GetAsync(retired.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ComponentVersion> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.ComponentVersions
            .Include(item => item.ComponentDefinition)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Component version '{id}' was not found.");
    }
}
