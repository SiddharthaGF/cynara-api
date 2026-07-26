using Cynara.Application.Modules.ClinicalTaxonomy;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped clinical area endpoints. Clinical areas are scoped to a
/// facility (Facility → ClinicalArea → Discipline). Ownership cannot be
/// transferred by PATCH; clients must delete and recreate to re-parent.
/// </summary>
[ApiController]
[Route("api/clinicalAreas")]
[Tags("Clinical Areas")]
public sealed class ClinicalAreasController(
    IClinicalTaxonomyService taxonomyService,
    IHttpContextAccessor httpContextAccessor) : ClinicalTaxonomyControllerBase(httpContextAccessor)
{
    /// <summary>Lists clinical area definitions owned by the resolved hospital workspace.</summary>
    [HttpGet(Name = "listClinicalAreas")]
    [EndpointDescription(
        "Lists clinical area definitions owned by the resolved hospital "
        + "workspace. Pass facilityId as a query parameter to filter by the "
        + "parent facility; pass includeRetired=true to surface retired rows.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalAreaListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicalAreaListResponse>> ListAsync(
        [FromQuery] Guid? facilityId,
        [FromQuery(Name = "includeRetired")] bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ClinicalAreaDto> areas = await taxonomyService
            .ListClinicalAreasAsync(facilityId, includeRetired, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new ClinicalAreaListResponse(areas));
    }

    /// <summary>Creates a new clinical area under the resolved hospital workspace.</summary>
    /// <exception cref="Application.ValidationException"></exception>
    [HttpPost(Name = "createClinicalArea")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalAreaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalAreaDto>> CreateAsync(
        CancellationToken cancellationToken)
    {
        CreateClinicalAreaRequest? request = await ReadJsonAsync<CreateClinicalAreaRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        ClinicalAreaDto created = await taxonomyService
            .CreateClinicalAreaAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/clinicalAreas/{created.Id}", created);
    }

    /// <summary>Updates the mutable display fields on an existing clinical area.</summary>
    [HttpPatch("{id:guid}", Name = "patchClinicalArea")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalAreaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalAreaDto>> PatchAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        UpdateClinicalAreaRequest? request = await ReadJsonAsync<UpdateClinicalAreaRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        ClinicalAreaDto updated = await taxonomyService
            .UpdateClinicalAreaAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(updated);
    }

    /// <summary>
    /// Retires an existing clinical area. Retired definitions remain
    /// resolvable for historical references.
    /// </summary>
    [HttpPost("{id:guid}/retire", Name = "retireClinicalArea")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalAreaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalAreaDto>> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        RetireClinicalAreaRequest? request = await ReadJsonAsync<RetireClinicalAreaRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? new RetireClinicalAreaRequest(0);
        ClinicalAreaDto retired = await taxonomyService
            .RetireClinicalAreaAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(retired);
    }
}

/// <summary>JSON-API-free collection response for clinical area listings.</summary>
public sealed record ClinicalAreaListResponse(
    IReadOnlyList<ClinicalAreaDto> ClinicalAreas);
