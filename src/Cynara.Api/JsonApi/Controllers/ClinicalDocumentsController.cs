using Cynara.Api.CapabilityAuthorization;
using Cynara.Application.Modules.Documents;
using Cynara.Domain.Capabilities;

using Microsoft.AspNetCore.Mvc;

namespace Cynara.Api.JsonApi.Controllers;

/// <summary>
/// Tenant-scoped clinical document instance endpoints. Bound to the
/// resolved hospital workspace; clients cannot move documents between
/// tenants. Body shapes mirror the application services so OpenAPI
/// documents the resources and errors without JSON:API projection.
/// </summary>
[ApiController]
[Route("api/clinicalDocuments")]
[Tags("Clinical Documents")]
public sealed class ClinicalDocumentsController(
    IClinicalDocumentService documentService,
    IHttpContextAccessor httpContextAccessor)
    : ClinicalDocumentControllerBase(httpContextAccessor)
{
    /// <summary>
    /// Lists document instances for the resolved hospital workspace.
    /// Terminal states remain included so historical records stay readable.
    /// </summary>
    [HttpGet(Name = "listClinicalDocuments")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsRead)]
    [EndpointDescription(
        "Lists clinical document instances for the resolved hospital "
        + "workspace. Filter by encounterId, patientId, "
        + "documentDefinitionId, or status. Completed documents remain "
        + "queryable.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClinicalDocumentListResponse>> ListAsync(
        [FromQuery] Guid? encounterId,
        [FromQuery] Guid? patientId,
        [FromQuery] Guid? documentDefinitionId,
        [FromQuery] string? status,
        CancellationToken cancellationToken = default)
    {
        ClinicalDocumentListRequest request = new(
            encounterId, patientId, documentDefinitionId, status);
        IReadOnlyList<ClinicalDocumentDto> matches = await documentService
            .ListAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new ClinicalDocumentListResponse(matches));
    }

    /// <summary>
    /// Returns the document instance matching the supplied identifier.
    /// </summary>
    [HttpGet("{id:guid}", Name = "getClinicalDocument")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsRead)]
    [EndpointDescription(
        "Returns the clinical document matching the supplied identifier "
        + "within the resolved hospital workspace. Completed documents "
        + "remain queryable.")]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClinicalDocumentDto>> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        ClinicalDocumentDto document = await documentService
            .GetAsync(id, cancellationToken)
            .ConfigureAwait(false);
        return Ok(document);
    }

    /// <summary>
    /// Starts a new clinical document under the resolved hospital workspace.
    /// Rejects retired catalog entries, non-open encounters, unpublished
    /// form versions, and duplicate instances for single-instance catalog
    /// entries.
    /// </summary>
    /// <exception cref="Application.ValidationException">
    /// Thrown when the request body is missing or fails validation.
    /// </exception>
    [HttpPost(Name = "startClinicalDocument")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalDocumentDto>> StartAsync(
        CancellationToken cancellationToken)
    {
        StartClinicalDocumentRequest? request = await ReadJsonAsync<StartClinicalDocumentRequest>(
            cancellationToken).ConfigureAwait(false)
            ?? throw new Application.ValidationException(
                "Request body is required.");
        ClinicalDocumentDto created = await documentService
            .StartAsync(request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Created($"/api/clinicalDocuments/{created.Id}", created);
    }

    /// <summary>
    /// Completes an in-progress clinical document. The bound form response
    /// is validated in complete mode and completed in the same transaction,
    /// so the recorded content becomes immutable.
    /// </summary>
    [HttpPost("{id:guid}/complete", Name = "completeClinicalDocument")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalDocumentDto>> CompleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionClinicalDocumentRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        ClinicalDocumentDto completed = await documentService
            .CompleteAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(completed);
    }

    /// <summary>Cancels an in-progress clinical document.</summary>
    [HttpPost("{id:guid}/cancel", Name = "cancelClinicalDocument")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalDocumentDto>> CancelAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionClinicalDocumentRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        ClinicalDocumentDto canceled = await documentService
            .CancelAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(canceled);
    }

    /// <summary>
    /// Marks an in-progress or completed clinical document as
    /// entered-in-error. The record remains historically queryable with its
    /// reason, actor, and timestamp.
    /// </summary>
    [HttpPost("{id:guid}/enter-in-error", Name = "enterClinicalDocumentInError")]
    [RequireCapability(CapabilityCodes.ClinicalDocumentsWrite)]
    [Consumes(ContentType)]
    [Produces(ContentType)]
    [ProducesResponseType(typeof(ClinicalDocumentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ClinicalDocumentDto>> EnterInErrorAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        TransitionClinicalDocumentRequest request = await ReadTransitionAsync(
            cancellationToken).ConfigureAwait(false);
        ClinicalDocumentDto marked = await documentService
            .EnterInErrorAsync(id, request, ActorId(), cancellationToken)
            .ConfigureAwait(false);
        return Ok(marked);
    }

    private async Task<TransitionClinicalDocumentRequest> ReadTransitionAsync(
        CancellationToken cancellationToken)
    {
        return await ReadJsonAsync<TransitionClinicalDocumentRequest>(
                cancellationToken)
            .ConfigureAwait(false)
            ?? new TransitionClinicalDocumentRequest(0);
    }
}
