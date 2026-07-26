using Cynara.Application.Modules.ClinicalTaxonomy;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped discipline endpoints. Disciplines are scoped to a clinical
/// area (Facility → ClinicalArea → Discipline).
/// </summary>
[ApiController]
[Route("api/disciplines")]
[Tags("Disciplines")]
public sealed class DisciplinesController(
    IClinicalTaxonomyService taxonomyService,
    IHttpContextAccessor httpContextAccessor) : ClinicalTaxonomyControllerBase(httpContextAccessor)
{
    /// <summary>Lists discipline definitions owned by the resolved hospital workspace.</summary>
    [HttpGet(Name = "listDisciplines")]
    [EndpointDescription(
        "Lists discipline definitions owned by the resolved hospital "
        + "workspace. Pass clinicalAreaId as a query parameter to filter by "
        + "the parent clinical area; pass includeRetired=true to surface "
        + "retired rows.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(DisciplineListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DisciplineListResponse>> ListAsync(
        [FromQuery] Guid? clinicalAreaId,
        [FromQuery(Name = "includeRetired")] bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DisciplineDto> disciplines = await taxonomyService
            .ListDisciplinesAsync(clinicalAreaId, includeRetired, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new DisciplineListResponse(disciplines));
    }

    /// <summary>Creates a new discipline under the resolved hospital workspace.</summary>
    [HttpPost(Name = "createDiscipline")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(DisciplineDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisciplineDto>> CreateAsync(
        CancellationToken cancellationToken)
    {
        CreateDisciplineRequest? request = await ReadJsonAsync<CreateDisciplineRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        DisciplineDto created = await taxonomyService
            .CreateDisciplineAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/disciplines/{created.Id}", created);
    }

    /// <summary>Updates the mutable display fields on an existing discipline.</summary>
    [HttpPatch("{id:guid}", Name = "patchDiscipline")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(DisciplineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisciplineDto>> PatchAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        UpdateDisciplineRequest? request = await ReadJsonAsync<UpdateDisciplineRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        DisciplineDto updated = await taxonomyService
            .UpdateDisciplineAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(updated);
    }

    /// <summary>
    /// Retires an existing discipline. Retired definitions remain
    /// resolvable for historical references.
    /// </summary>
    [HttpPost("{id:guid}/retire", Name = "retireDiscipline")]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(DisciplineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DisciplineDto>> RetireAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        RetireDisciplineRequest? request = await ReadJsonAsync<RetireDisciplineRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? new RetireDisciplineRequest(0);
        DisciplineDto retired = await taxonomyService
            .RetireDisciplineAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(retired);
    }
}

/// <summary>JSON-API-free collection response for discipline listings.</summary>
public sealed record DisciplineListResponse(
    IReadOnlyList<DisciplineDto> Disciplines);
