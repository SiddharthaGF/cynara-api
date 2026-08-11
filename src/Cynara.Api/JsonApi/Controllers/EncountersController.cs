using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Encounters;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped clinical encounter endpoints. Bound to the resolved
/// hospital workspace; clients cannot move encounters between tenants.
/// Body shapes mirror the application services so OpenAPI documents the
/// resources, transitions, and errors without JSON:API projection.
/// </summary>
[ApiController]
[Route("api/encounters")]
[Tags("Encounters")]
public sealed class EncountersController(
    IEncounterService encounterService,
    ISensitiveReadAuditor sensitiveReadAuditor,
    IHttpContextAccessor httpContextAccessor)
    : JsonApiCrudControllerBase(httpContextAccessor)
{
    /// <summary>
    /// Lists encounters for the resolved hospital workspace. Terminal
    /// states remain included so historical records stay readable.
    /// </summary>
    [HttpGet(Name = "listEncounters")]
    [RequireCapability(CapabilityCodes.EncountersRead)]
    [EndpointDescription(
        "Lists encounters for the resolved hospital workspace. Filter by "
        + "patientId, facilityId, clinicalAreaId, or status. Completed, "
        + "canceled, and entered-in-error encounters remain queryable.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EncounterListResponse>> ListAsync(
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? facilityId,
        [FromQuery] Guid? clinicalAreaId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        EncounterListRequest request = new(
            patientId, facilityId, clinicalAreaId, status);
        IReadOnlyList<EncounterDto> matches = await encounterService
            .ListAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new EncounterListResponse(matches));
    }

    /// <summary>Returns the encounter matching the supplied identifier.</summary>
    [HttpGet("{id:guid}", Name = "getEncounter")]
    [RequireCapability(CapabilityCodes.EncountersRead)]
    [EndpointDescription(
        "Returns the encounter matching the supplied identifier within the "
        + "resolved hospital workspace. Terminal states remain queryable.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EncounterDto>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        EncounterDto encounter = await encounterService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        await sensitiveReadAuditor.RecordAsync(
            AuditEntityTypes.Encounter,
            encounter.Id,
            "encounter.read",
            ActorId(),
            HttpContext.Request.Path,
            cancellationToken).ConfigureAwait(false);
        return Ok(encounter);
    }

    /// <summary>
    /// Creates a new open encounter under the resolved hospital workspace.
    /// Cross-tenant or retired references are rejected.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPost(Name = "createEncounter")]
    [RequireCapability(CapabilityCodes.EncountersWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EncounterDto>> CreateAsync(
        CancellationToken cancellationToken)
    {
        CreateEncounterRequest? request = await ReadJsonAsync<CreateEncounterRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        EncounterDto created = await encounterService
            .CreateAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/encounters/{created.Id}", created);
    }

    /// <summary>Completes an open encounter.</summary>
    [HttpPost("{id:guid}/complete", Name = "completeEncounter")]
    [RequireCapability(CapabilityCodes.EncountersWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EncounterDto>> CompleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionEncounterRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        EncounterDto completed = await encounterService
            .CompleteAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(completed);
    }

    /// <summary>Cancels an open encounter.</summary>
    [HttpPost("{id:guid}/cancel", Name = "cancelEncounter")]
    [RequireCapability(CapabilityCodes.EncountersWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EncounterDto>> CancelAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionEncounterRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        EncounterDto canceled = await encounterService
            .CancelAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(canceled);
    }

    /// <summary>
    /// Marks an open encounter as entered-in-error. The record remains
    /// historically queryable.
    /// </summary>
    [HttpPost("{id:guid}/enter-in-error", Name = "enterEncounterInError")]
    [RequireCapability(CapabilityCodes.EncountersWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(EncounterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EncounterDto>> EnterInErrorAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionEncounterRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        EncounterDto marked = await encounterService
            .EnterInErrorAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(marked);
    }

    private async Task<TransitionEncounterRequest> ReadTransitionAsync(
        CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<TransitionEncounterRequest>(cancellationToken)
            .ConfigureAwait(false)
            ?? new TransitionEncounterRequest(0);
    }
}
