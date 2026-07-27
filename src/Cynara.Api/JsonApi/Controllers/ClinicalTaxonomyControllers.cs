using Cynara.Application.Modules.ClinicalTaxonomy;
using Cynara.Application.Modules.Hospitals;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped facility endpoints. Bound to the resolved hospital
/// workspace via <see cref="IHospitalContext"/>; clients cannot move the
/// record between tenants through this surface. Body shapes mirror the
/// application services exactly so the OpenAPI documentation can be
/// authored without <c>JsonApiDotNetCore</c> attribute projection.
/// </summary>
[ApiController]
[Route("api/facilities")]
[Tags("Facilities")]
public sealed class FacilitiesController(
    IClinicalTaxonomyService taxonomyService,
    IHttpContextAccessor httpContextAccessor) : ClinicalTaxonomyControllerBase(httpContextAccessor)
{
    /// <summary>
    /// Lists facility definitions owned by the resolved hospital workspace.
    /// </summary>
    [HttpGet(Name = "listFacilities")]
    [EndpointDescription(
        "Lists facility definitions owned by the resolved hospital workspace. "
        + "Tenant failures (missing X-Hospital-Code, unknown code, inactive "
        + "hospital) are surfaced before this endpoint runs.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(FacilityListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<FacilityListResponse>> ListAsync(
        [FromQuery(Name = "includeRetired")] bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FacilityDto> facilities = await taxonomyService
            .ListFacilitiesAsync(includeRetired, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new FacilityListResponse(facilities));
    }

    /// <summary>
    /// Creates a new facility under the resolved hospital workspace.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPost(Name = "createFacility")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FacilityDto>> CreateAsync(
        CancellationToken cancellationToken)
    {
        CreateFacilityRequest? request = await ReadJsonAsync<CreateFacilityRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        FacilityDto created = await taxonomyService
            .CreateFacilityAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/facilities/{created.Id}", created);
    }

    /// <summary>
    /// Updates the mutable display fields on an existing facility.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPatch("{id:guid}", Name = "patchFacility")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FacilityDto>> PatchAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        UpdateFacilityRequest? request = await ReadJsonAsync<UpdateFacilityRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        FacilityDto updated = await taxonomyService
            .UpdateFacilityAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(updated);
    }

    /// <summary>
    /// Retires an existing facility. Retired definitions remain resolvable
    /// for historical records.
    /// </summary>
    [HttpPost("{id:guid}/retire", Name = "retireFacility")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(FacilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FacilityDto>> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        RetireFacilityRequest? request = await ReadJsonAsync<RetireFacilityRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? new RetireFacilityRequest(0);
        FacilityDto retired = await taxonomyService
            .RetireFacilityAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(retired);
    }
}

/// <summary>
/// JSON-API-free collection response for facility listings. Kept as a
/// positional record so the controller can serialise without depending on
/// <c>JsonApiDotNetCore</c> resource envelopes.
/// </summary>
public sealed record FacilityListResponse(
    IReadOnlyList<FacilityDto> Facilities);
