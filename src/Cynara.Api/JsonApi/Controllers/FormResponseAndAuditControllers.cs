using Cynara.Api.Common.ActorContext;
using Cynara.Application.Forms;
using Cynara.Application.Modules.FormResponses;
using Cynara.Domain.Forms;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Controllers;
using JsonApiDotNetCore.Services;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// JSON:API controller for form responses plus complete transition.
/// </summary>
[Route("api/formResponses")]
public sealed class FormResponsesController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<FormResponse, Guid> resourceService,
    IFormResponseLifecycleService lifecycle,
    IHttpContextAccessor httpContextAccessor) : JsonApiController<FormResponse, Guid>(options, resourceGraph, loggerFactory, resourceService)
{
    /// <summary>
    /// Completes a draft response after full validation against the published
    /// form version schemas and rules.
    /// </summary>
    [HttpPost("{id}/complete", Name = "completeFormResponse")]
    [EndpointDescription(
        "Completes a draft form response. Pass rowVersion as a query parameter "
        + "and runs complete-mode validation. No request body.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CompleteAsync(
        Guid id,
        [FromQuery] uint rowVersion,
        CancellationToken cancellationToken)
    {
        FormResponseDto completed = await lifecycle.CompleteAsync(
            id,
            new CompleteFormResponseRequest(rowVersion),
            httpContextAccessor.HttpContext?.GetActorId(),
            cancellationToken).ConfigureAwait(false);
        return await GetAsync(completed.Id, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Read-only JSON:API controller for response revisions.</summary>
[Route("api/formResponseRevisions")]
public sealed class FormResponseRevisionsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<FormResponseRevision, Guid> resourceService)
     : JsonApiController<FormResponseRevision, Guid>(options, resourceGraph, loggerFactory, resourceService);

/// <summary>Read-only JSON:API controller for audit events.</summary>
[Route("api/auditEvents")]
public sealed class AuditEventsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<Domain.Audit.AuditEvent, Guid> resourceService)
        : JsonApiController<Domain.Audit.AuditEvent, Guid>(options, resourceGraph, loggerFactory, resourceService);

/// <summary>JSON:API controller for AI provider settings singleton.</summary>
[Route("api/aiProviderSettings")]
public sealed class AiProviderSettingsController(
    IJsonApiOptions options,
    IResourceGraph resourceGraph,
    ILoggerFactory loggerFactory,
    IResourceService<Domain.FormAi.AiProviderSettings, string> resourceService)
        : JsonApiController<Domain.FormAi.AiProviderSettings, string>(options, resourceGraph, loggerFactory, resourceService);
