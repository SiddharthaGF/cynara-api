using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Forms;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API controller for form versions plus review/publish/retire actions.
/// Workflow commands use query parameters so JsonApiDotNetCore does not
/// attempt to deserialize a JSON:API resource document body.
/// </summary>
[Route("api/formVersions")]
public sealed class FormVersionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<FormVersion, Guid> resourceService,
    IFormService formService,
    IFormReviewService reviewService,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext) : JsonApiController<FormVersion, Guid>(options, resourceGraph, loggerFactory, resourceService)
{
    /// <summary>
    /// Submits the editable draft for review. Locks schema edits until
    /// withdraw or reject.
    /// </summary>
    [HttpPost("{id}/submit-review")]
    [EndpointDescription(
        "Transitions a draft form version to review. Pass rowVersion as a "
        + "query parameter for optimistic concurrency.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> SubmitReviewAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            id,
            (code, actor) => reviewService.SubmitForReviewAsync(
                code,
                new SubmitFormDraftForReviewRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Withdraws a version from review back to draft.
    /// </summary>
    [HttpPost("{id}/withdraw-review")]
    [EndpointDescription(
        "Returns a form version from review to draft. Pass rowVersion as query.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> WithdrawReviewAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            id,
            (code, actor) => reviewService.WithdrawFromReviewAsync(
                code,
                new WithdrawFormDraftFromReviewRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Rejects a review with a required comment and returns to draft.
    /// </summary>
    [HttpPost("{id}/reject-review")]
    [EndpointDescription(
        "Rejects a form version in review. Requires comment and rowVersion "
        + "query parameters.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> RejectReviewAsync(
        Guid id,
        [FromQuery] string comment,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            id,
            (code, actor) => reviewService.RejectReviewAsync(
                code,
                new RejectFormReviewRequest(comment, rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Publishes a form version after review, assigning a semver and hash.
    /// </summary>
    [HttpPost("{id}/publish")]
    [EndpointDescription(
        "Publishes a form version after review approval. Pass rowVersion as query.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<IActionResult> PublishAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        return RunAsync(
            id,
            (code, actor) => reviewService.PublishDraftAsync(
                code,
                new PublishFormDraftRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Retires a published form version so it no longer accepts new responses.
    /// </summary>
    /// <exception cref="Application.InvalidStateException">
    /// Thrown when the version is not published.
    /// </exception>
    [HttpPost("{id}/retire")]
    [EndpointDescription(
        "Retires a published form version. Retired versions remain readable.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        FormVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version.Version))
        {
            throw new Application.InvalidStateException(
                "Only published versions with a semver can be retired.");
        }

        string? actor = httpContextAccessor.HttpContext?.GetActorId();
        FormVersionDto retired = await formService.RetireVersionAsync(
            version.FormDefinition.Code,
            version.Version,
            actor,
            cancellationToken).ConfigureAwait(false);
        return await GetAsync(retired.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> RunAsync(
        Guid id,
        Func<string, string?, Task<FormVersionDto>> action,
        CancellationToken cancellationToken)
    {
        FormVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        string? actor = httpContextAccessor.HttpContext?.GetActorId();
        FormVersionDto dto = await action(
            version.FormDefinition.Code,
            actor).ConfigureAwait(false);
        return await GetAsync(dto.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<FormVersion> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        FormVersion version = await dbContext.FormVersions
            .Include(item => item.FormDefinition)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");

        if (version.HospitalId != hospitalContext.HospitalId
            || version.FormDefinition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Form version '{id}' was not found.");
        }

        return version;
    }
}
