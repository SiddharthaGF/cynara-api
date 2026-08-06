using Cynara.Api.Common.ActorContext;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Workflows;
using Cynara.Application.Workflows;
using Cynara.Domain.Workflows;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API controller for workflow versions plus review/publish/retire
/// actions. Workflow commands use query parameters so JsonApiDotNetCore does
/// not attempt to deserialize a JSON:API resource document body.
/// </summary>
[Route("api/workflowVersions")]
public sealed class WorkflowVersionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<WorkflowVersion, Guid> resourceService,
    IWorkflowLifecycleService lifecycle,
    IHospitalContext hospitalContext,
    IHttpContextAccessor httpContextAccessor,
    CynaraDbContext dbContext) : JsonApiController<WorkflowVersion, Guid>(
        options,
        resourceGraph,
        loggerFactory,
        resourceService)
{
    /// <summary>
    /// Submits the editable draft for review. Locks schema edits until
    /// withdraw or reject.
    /// </summary>
    [HttpPost("{id}/submit-review", Name = "submitWorkflowReview")]
    [EndpointDescription(
        "Transitions a draft workflow version to review. Pass rowVersion as a "
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
            (code, actor) => lifecycle.SubmitForReviewAsync(
                code,
                new SubmitWorkflowDraftForReviewRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Withdraws a version from review back to draft.
    /// </summary>
    [HttpPost("{id}/withdraw-review", Name = "withdrawWorkflowReview")]
    [EndpointDescription(
        "Returns a workflow version from review to draft. Pass rowVersion as query.")]
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
            (code, actor) => lifecycle.WithdrawFromReviewAsync(
                code,
                new WithdrawWorkflowDraftFromReviewRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Rejects a review with a required comment and returns to draft.
    /// </summary>
    [HttpPost("{id}/reject-review", Name = "rejectWorkflowReview")]
    [EndpointDescription(
        "Rejects a workflow version in review. Requires comment and rowVersion "
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
            (code, actor) => lifecycle.RejectReviewAsync(
                code,
                new RejectWorkflowReviewRequest(comment, rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Publishes a workflow version after review, assigning a semver and hash.
    /// </summary>
    [HttpPost("{id}/publish", Name = "publishWorkflowVersion")]
    [EndpointDescription(
        "Publishes a workflow version after review approval. Pass rowVersion "
        + "as query.")]
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
            (code, actor) => lifecycle.PublishDraftAsync(
                code,
                new PublishWorkflowDraftRequest(rowVersion),
                actor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Retires a published workflow version so it no longer starts new
    /// pipelines. Retired versions remain readable.
    /// </summary>
    /// <exception cref="Application.InvalidStateException">
    /// Thrown when the version is not published.
    /// </exception>
    [HttpPost("{id}/retire", Name = "retireWorkflowVersion")]
    [EndpointDescription(
        "Retires a published workflow version. Retired versions remain readable.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        WorkflowVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version.Version))
        {
            throw new Application.InvalidStateException(
                "Only published versions with a semver can be retired.");
        }

        WorkflowVersionDto retired = await lifecycle.RetireVersionAsync(
            version.WorkflowDefinition.Code,
            version.Version,
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return await GetAsync(retired.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> RunAsync(
        Guid id,
        Func<string, string?, Task<WorkflowVersionDto>> action,
        CancellationToken cancellationToken)
    {
        WorkflowVersion version = await LoadAsync(id, cancellationToken)
            .ConfigureAwait(false);
        string? actor = httpContextAccessor.HttpContext?.GetActorId();
        WorkflowVersionDto dto = await action(
            version.WorkflowDefinition.Code,
            actor).ConfigureAwait(false);
        return await GetAsync(dto.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WorkflowVersion> LoadAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        WorkflowVersion version = await dbContext.WorkflowVersions
            .Include(item => item.WorkflowDefinition)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");

        if (version.HospitalId != hospitalContext.HospitalId
            || version.WorkflowDefinition.HospitalId != hospitalContext.HospitalId)
        {
            throw new Application.NotFoundException(
                $"Workflow version '{id}' was not found.");
        }

        return version;
    }
}
