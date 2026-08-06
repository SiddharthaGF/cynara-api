using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Application;
using Cynara.Application.Modules.Capabilities;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Capability administration endpoints. Grants and revocations are scoped to
/// the resolved hospital workspace, so assignments created here can only ever
/// authorize access within this tenant. Reads require capabilities.read;
/// mutations require capabilities.write.
/// </summary>
[ApiController]
[Route("api/capabilities")]
[Tags("Capabilities")]
[RequireCapability(CapabilityCodes.CapabilitiesRead)]
public sealed class CapabilityAssignmentsController(
    ICapabilityAssignmentService service,
    IHttpContextAccessor httpContextAccessor) : ControllerBase
{
    private const string ContentType = "application/vnd.api+json";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Lists every capability assignment for the resolved hospital
    /// workspace, newest first.
    /// </summary>
    [HttpGet(Name = "listCapabilityAssignments")]
    [EndpointDescription(
        "Lists every capability assignment in the resolved hospital "
        + "workspace, newest first. The grant surface is tenant-scoped; "
        + "actors from other hospitals never appear.")]
    [Produces(ContentType)]
    [ProducesResponseType(
        typeof(CapabilityAssignmentListResponse),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<CapabilityAssignmentListResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        CapabilityAssignmentListResponse matches = await service
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);
        return Ok(matches);
    }

    /// <summary>
    /// Grants a capability to an actor within the resolved hospital
    /// workspace.
    /// </summary>
    /// <exception cref="ValidationException">
    /// Thrown when the request body is missing, the actor is empty, or the
    /// capability code is not part of the Stage 2 catalog.
    /// </exception>
    [HttpPost(Name = "grantCapability")]
    [RequireCapability(CapabilityCodes.CapabilitiesWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(
        typeof(CapabilityAssignmentDto),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CapabilityAssignmentDto>> GrantAsync(
        CancellationToken cancellationToken)
    {
        GrantCapabilityRequest? request = await ReadJsonAsync<GrantCapabilityRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException(
                "Request body is required.");
        CapabilityAssignmentDto created = await service
            .GrantAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created(
            $"/api/capabilities/{created.ActorId}/{created.Capability}",
            created);
    }

    /// <summary>
    /// Revokes a capability from an actor within the resolved hospital
    /// workspace.
    /// </summary>
    [HttpDelete("{actorId}/{capability}", Name = "revokeCapability")]
    [RequireCapability(CapabilityCodes.CapabilitiesWrite)]
    [Produces(ContentType)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(
        string actorId,
        string capability,
        CancellationToken cancellationToken)
    {
        await service
            .RevokeAsync(actorId, capability, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return NoContent();
    }

    private async Task<T?> ReadJsonAsync<T>(CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await System.Text.Json.JsonSerializer
                .DeserializeAsync<T>(
                    Request.Body,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ValidationException(
                $"Request body rejected: {exception.Message}");
        }
    }

    private string? ActorId()
    {
        return httpContextAccessor.HttpContext?.GetActorId();
    }
}
