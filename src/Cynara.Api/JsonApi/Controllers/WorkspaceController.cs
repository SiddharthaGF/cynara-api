using System.Text.Json;

using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Workspace endpoint bound to the resolved tenant. The route is fixed and
/// does not accept a hospital selector: clients cannot choose or override the
/// tenant through this surface.
/// </summary>
[ApiController]
[Route("api/workspace")]
[Tags("Workspace")]
public sealed class WorkspaceController(
    IHospitalWorkspaceService workspaceService) : ControllerBase
{
    private const string MissingHeaderExample =
        "Example body:\n"
        + "{ \"errors\": [{ \"status\": \"400\", \"title\": \"Hospital context required\", "
        + "\"detail\": \"Missing X-Hospital-Code header. Provide a known hospital code.\" }] }";

    private const string InactiveHospitalExample =
        "Example body:\n"
        + "{ \"errors\": [{ \"status\": \"403\", \"title\": \"Hospital workspace unavailable\", "
        + "\"detail\": \"Hospital 'default' is suspended.\" }] }";

    private const string MalformedBodyExample =
        "Malformed or unreadable request body. Tenant failures (missing or "
        + "unknown X-Hospital-Code, inactive hospital) return the JSON:API "
        + "error document described on GET.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Returns the current hospital workspace. The route does not take a
    /// hospital identifier; the resolved tenant is the only source of truth.
    /// </summary>
    /// <remarks>
    /// Tenant failures (missing X-Hospital-Code, unknown code, inactive
    /// hospital) are surfaced as JSON:API error objects (see the 400/403
    /// response descriptions for concrete payloads).
    /// </remarks>
    [HttpGet(Name = "getWorkspace")]
    [RequireCapability(CapabilityCodes.WorkspaceRead)]
    [EndpointDescription(
        "Returns the hospital workspace resolved from the "
        + "X-Hospital-Code header. The endpoint is bound to the request "
        + "context; clients cannot select a different tenant.")]
    [Produces("application/vnd.api+json")]
    [ProducesResponseType(typeof(HospitalWorkspaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(JsonApiErrorDocument),
        StatusCodes.Status400BadRequest,
        Description = MissingHeaderExample)]
    [ProducesResponseType(
        typeof(JsonApiErrorDocument),
        StatusCodes.Status403Forbidden,
        Description = InactiveHospitalExample)]
    public async Task<ActionResult<HospitalWorkspaceDto>> GetAsync(
        CancellationToken cancellationToken)
    {
        HospitalWorkspaceDto workspace = await workspaceService
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        return Ok(workspace);
    }

    /// <summary>
    /// Updates the current hospital workspace. The contract accepts only
    /// mutable display fields and a concurrency token. Hospital identifier,
    /// code, and creation timestamp are immutable.
    /// </summary>
    /// <remarks>
    /// Requests that try to override immutable fields
    /// (<c>hospitalId</c>, <c>tenantId</c>, <c>id</c>, <c>code</c>,
    /// <c>createdAt</c>) are rejected with 400. Stale <c>rowVersion</c>
    /// values return 409. Tenant failure payloads are described inline on
    /// the 400/403 response codes.
    /// </remarks>
    [HttpPatch(Name = "patchWorkspace")]
    [RequireCapability(CapabilityCodes.WorkspaceWrite)]
    [Consumes("application/vnd.api+json")]
    [Produces("application/vnd.api+json")]
    [ProducesResponseType(typeof(HospitalWorkspaceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status400BadRequest,
        Description = MalformedBodyExample)]
    [ProducesResponseType(
        typeof(JsonApiErrorDocument),
        StatusCodes.Status403Forbidden,
        Description = InactiveHospitalExample)]
    [ProducesResponseType(
        typeof(JsonApiErrorDocument),
        StatusCodes.Status409Conflict,
        Description = "rowVersion does not match the persisted workspace.")]
    public async Task<ActionResult<HospitalWorkspaceDto>> PatchAsync(
        CancellationToken cancellationToken)
    {
        UpdateHospitalWorkspaceRequest? request;
        try
        {
            request = await ReadBodyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return BadRequest(TenantValidationError(
                $"Workspace update rejected: {exception.Message}"));
        }

        if (request is null)
        {
            return BadRequest(TenantValidationError("Request body is required."));
        }

        HospitalWorkspaceDto workspace = await workspaceService
            .UpdateCurrentAsync(
                request,
                HttpContext.GetActorId(),
                cancellationToken)
            .ConfigureAwait(false);
        return Ok(workspace);
    }

    private async Task<UpdateHospitalWorkspaceRequest?> ReadBodyAsync(
        CancellationToken cancellationToken)
    {
        return await JsonSerializer
            .DeserializeAsync<UpdateHospitalWorkspaceRequest>(
                Request.Body,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProblemDetails TenantValidationError(string detail)
    {
        return new ProblemDetails
        {
            Title = "Validation failed",
            Detail = detail,
            Status = StatusCodes.Status400BadRequest,
        };
    }
}
