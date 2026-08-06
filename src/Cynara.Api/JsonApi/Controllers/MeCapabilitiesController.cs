using Cynara.Application.Modules.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Returns the effective capability set for the current actor within the
/// resolved hospital workspace. Readable by any actor with a hospital
/// context; it exists so clients can drive UI affordances from the same
/// source the API enforces.
/// </summary>
[ApiController]
[Route("api/me/capabilities")]
[Tags("Capabilities")]
public sealed class MeCapabilitiesController(
    EffectiveCapabilityResolver resolver,
    ICurrentActor currentActor) : ControllerBase
{
    /// <summary>
    /// Returns the effective capabilities of the current actor. An actor with
    /// no grant (or no <c>X-Actor-Id</c>) receives an empty list, mirroring
    /// the deny-by-default behavior of the enforcement layer.
    /// </summary>
    [HttpGet(Name = "getMyCapabilities")]
    [EndpointDescription(
        "Returns the effective capability codes of the current actor within "
        + "the resolved hospital workspace. The same resolution the API "
        + "enforces drives this response, so clients can mirror the server's "
        + "authorization posture.")]
    [Produces("application/vnd.api+json")]
    [ProducesResponseType(
        typeof(MeCapabilitiesResponse),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<MeCapabilitiesResponse>> GetAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlySet<string> effective = await resolver
            .ResolveAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<string> codes = [.. effective.Order(StringComparer.Ordinal)];
        return Ok(new MeCapabilitiesResponse(
            currentActor.ActorId,
            codes));
    }
}
