using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Patients;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped patient registry endpoints. Bound to the resolved hospital
/// workspace via <see cref="IHospitalContext"/>; clients cannot move
/// patient records between tenants through this surface. Body shapes
/// mirror the application services exactly so the OpenAPI documentation
/// can be authored without <c>JsonApiDotNetCore</c> attribute projection.
/// </summary>
[ApiController]
[Route("api/patients")]
[Tags("Patients")]
public sealed class PatientsController(
    IPatientService patientService,
    ISensitiveReadAuditor sensitiveReadAuditor,
    IHttpContextAccessor httpContextAccessor) : JsonApiCrudControllerBase(httpContextAccessor)
{
    /// <summary>
    /// Searches the patient roster for the resolved hospital workspace.
    /// Soft-deleted records are hidden unless
    /// <c>includeDeleted=true</c> is supplied.
    /// </summary>
    [HttpGet(Name = "searchPatients")]
    [RequireCapability(CapabilityCodes.PatientsRead)]
    [EndpointDescription(
        "Searches the patient roster for the resolved hospital workspace. "
        + "Tenant failures (missing X-Hospital-Code, unknown code, inactive "
        + "hospital) are surfaced before this endpoint runs.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(PatientListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatientListResponse>> SearchAsync(
        [FromQuery] PatientSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        PatientSearchRequest request = new(
            query.Mrn,
            query.NationalId,
            query.GivenName,
            query.FamilyName,
            query.IncludeDeleted ?? false,
            query.Page ?? 1,
            query.PageSize ?? PatientFieldLimits.DefaultPageSize);
        PatientListResponse matches = await patientService
            .SearchAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Ok(matches);
    }

    /// <summary>Returns the patient matching the supplied identifier.</summary>
    [HttpGet("{id:guid}", Name = "getPatient")]
    [RequireCapability(CapabilityCodes.PatientsRead)]
    [EndpointDescription(
        "Returns the patient matching the supplied identifier within the "
        + "resolved hospital workspace. Soft-deleted patients return 404.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PatientDto>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        PatientDto patient = await patientService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        await sensitiveReadAuditor.RecordAsync(
            AuditEntityTypes.Patient,
            patient.Id,
            "patient.read",
            ActorId(),
            HttpContext.Request.Path,
            cancellationToken).ConfigureAwait(false);
        return Ok(patient);
    }

    /// <summary>
    /// Creates a new patient under the resolved hospital workspace. MRN is
    /// unique within the hospital; duplicates return 409.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPost(Name = "createPatient")]
    [RequireCapability(CapabilityCodes.PatientsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> CreateAsync(
        CancellationToken cancellationToken)
    {
        CreatePatientRequest? request = await ReadJsonAsync<CreatePatientRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        PatientDto created = await patientService
            .CreateAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/patients/{created.Id}", created);
    }

    /// <summary>
    /// Updates the mutable demographic fields on an existing patient. The
    /// MRN is immutable after creation.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPatch("{id:guid}", Name = "patchPatient")]
    [RequireCapability(CapabilityCodes.PatientsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> PatchAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        UpdatePatientRequest? request = await ReadJsonAsync<UpdatePatientRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        PatientDto updated = await patientService
            .UpdateAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(updated);
    }

    /// <summary>
    /// Soft-deletes an existing patient. The record is hidden from default
    /// search and detail responses but remains resolvable for historical
    /// form responses and audit continuity.
    /// </summary>
    [HttpPost("{id:guid}/soft-delete", Name = "softDeletePatient")]
    [RequireCapability(CapabilityCodes.PatientsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(PatientDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(JsonApiErrorDocument), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PatientDto>> SoftDeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        SoftDeletePatientRequest? request = await ReadJsonAsync<SoftDeletePatientRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? new SoftDeletePatientRequest(0);
        PatientDto deleted = await patientService
            .SoftDeleteAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(deleted);
    }
}

/// <summary>
/// Query filters for the patient search endpoint. Names and defaults mirror
/// the <see cref="PatientSearchRequest"/> contract so the OpenAPI surface
/// matches the service semantics exactly.
/// </summary>
public sealed class PatientSearchQuery
{
    [FromQuery(Name = "mrn")]
    public string? Mrn { get; init; }

    [FromQuery(Name = "nationalId")]
    public string? NationalId { get; init; }

    [FromQuery(Name = "givenName")]
    public string? GivenName { get; init; }

    [FromQuery(Name = "familyName")]
    public string? FamilyName { get; init; }

    [FromQuery(Name = "includeDeleted")]
    public bool? IncludeDeleted { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "pageSize")]
    public int? PageSize { get; init; }
}
