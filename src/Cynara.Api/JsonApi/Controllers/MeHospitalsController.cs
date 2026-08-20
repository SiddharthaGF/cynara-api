using Cynara.Api.Hosting;
using Cynara.Application.Modules.Hospitals;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Returns the hospital workspaces the authenticated user belongs to. This
/// surface is tenant-exempt: it requires a valid bearer token but no
/// <c>X-Hospital-Code</c> header, and it never resolves an actor or checks a
/// capability. It exists so clients can present the hospital switcher before
/// any tenant context exists.
/// </summary>
[ApiController]
[Route("api/me/hospitals")]
[Tags("Hospitals")]
public sealed class MeHospitalsController(
    HospitalMembershipService memberships) : ControllerBase
{
    /// <summary>
    /// Returns the code and name of every hospital the current user belongs
    /// to, ordered by hospital code. The user is resolved from the token
    /// <c>sub</c> claim; subjects that are not user ids (for example
    /// client-credentials clients) receive an empty collection, mirroring the
    /// deny-by-default posture of the resolution layer.
    /// </summary>
    [HttpGet(Name = "getMyHospitals")]
    [EndpointDescription(
        "Returns the hospital workspaces the authenticated user belongs to. "
        + "This endpoint is tenant-exempt: no X-Hospital-Code header is "
        + "required and no actor is selected, so clients can present the "
        + "hospital switcher before any tenant context exists. Each item "
        + "carries the hospital code and display name only.")]
    [Produces("application/vnd.api+json")]
    [ProducesResponseType(
        typeof(IReadOnlyList<HospitalMembershipDto>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HospitalMembershipDto>>> GetAsync(
        CancellationToken cancellationToken)
    {
        // OpenIddict does not apply the default inbound claim-type mapping,
        // so the subject claim is read by its literal name. Client-credentials
        // subjects are client identifiers (not user ids) and yield no rows.
        if (!PrincipalSubject.TryGetUserId(User, out Guid userId))
        {
            return Ok(Array.Empty<HospitalMembershipDto>());
        }

        IReadOnlyList<HospitalMembershipDto> items = await memberships
            .ListAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(items);
    }
}
