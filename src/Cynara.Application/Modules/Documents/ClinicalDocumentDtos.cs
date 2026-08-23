using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Public read shape for a clinical document instance. Identity and
/// provenance fields (definition, patient, encounter, pinned form version,
/// response) are immutable; <c>Status</c> is inProgress/completed/canceled/
/// enteredInError; <c>RowVersion</c> guards optimistic concurrency.
/// </summary>
public sealed record ClinicalDocumentDto(
    Guid Id,
    Guid DocumentDefinitionId,
    Guid PatientId,
    Guid EncounterId,
    Guid FormVersionId,
    Guid FormResponseId,
    string? AuthorId,
    [property: OpenApiEnumValues("inProgress", "completed", "canceled", "enteredInError")]
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CanceledAt,
    string? EnteredInErrorReason,
    string? EnteredInErrorById,
    DateTimeOffset? EnteredInErrorAt,
    DateTimeOffset UpdatedAt,
    uint RowVersion);

/// <summary>
/// Start contract for a clinical document instance. References must resolve
/// within the hospital workspace: the catalog entry must be active, the
/// encounter open, and the pinned form version still published.
/// </summary>
public sealed record StartClinicalDocumentRequest(
    Guid DocumentDefinitionId,
    Guid EncounterId);

/// <summary>
/// Transition contract for completing, canceling, or entering a clinical
/// document in error. The <c>RowVersion</c> must match the latest document
/// state; <c>Reason</c> is required when entering in error and ignored for
/// the other transitions.
/// </summary>
public sealed record TransitionClinicalDocumentRequest(
    uint RowVersion,
    string? Reason = null);

/// <summary>
/// List filter for clinical document instances. All criteria are optional;
/// an empty filter returns the hospital roster.
/// </summary>
public sealed record ClinicalDocumentListRequest(
    Guid? EncounterId = null,
    Guid? PatientId = null,
    Guid? DocumentDefinitionId = null,
    string? Status = null);

/// <summary>JSON-API-free collection response for document listings.</summary>
public sealed record ClinicalDocumentListResponse(
    IReadOnlyList<ClinicalDocumentDto> Documents);
