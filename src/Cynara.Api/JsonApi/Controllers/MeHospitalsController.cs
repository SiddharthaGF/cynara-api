using Cynara.Api.Hosting;
using Cynara.Application.Modules.Hospitals;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Returns the hospital workspaces the authenticated user belongs to.
/// Tenant-exempt: requires a valid bearer token but no <c>X-Hospital-Code</c>,
/// and never resolves an actor or checks a capability — it exists so clients
/// can present the hospital switcher before any tenant context exists.
/// </summary>
[ApiController]
[Route("api/me/hospitals")]
[Tags("Hospitals")]
public sealed class MeHospitalsController(
    HospitalMembershipService memberships) : ControllerBase
{
    /// <summary>
    /// Returns every hospital of the current user, resolved from the token
    /// <c>sub</c> claim (read literally; OpenIddict applies no inbound
    /// claim-type mapping). Client-credential subjects yield no rows.
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
