using Cynara.Application.OpenApi;

namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Public read and write shapes for clinical document instances.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>DocumentDefinitionId</c> – catalog entry the document was
/// started from; immutable.</item>
/// <item><c>PatientId</c> – patient from the bound encounter; immutable.</item>
/// <item><c>EncounterId</c> – encounter the document belongs to;
/// immutable.</item>
/// <item><c>FormVersionId</c> – exact published form version captured at
/// creation; immutable so historical documents stay resolvable.</item>
/// <item><c>FormResponseId</c> – form response carrying the document's
/// answers; immutable.</item>
/// <item><c>AuthorId</c> – actor that started the document; optional.</item>
/// <item><c>Status</c> – one of <c>inProgress</c>, <c>completed</c>,
/// <c>canceled</c>, <c>enteredInError</c>.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>CompletedAt</c> – UTC timestamp when completed; null while
/// in progress.</item>
/// <item><c>CanceledAt</c> – UTC timestamp when canceled; null unless
/// canceled.</item>
/// <item><c>EnteredInErrorReason</c> / <c>EnteredInErrorById</c> /
/// <c>EnteredInErrorAt</c> – read-only attribution for entered-in-error
/// records; null otherwise.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// future transitions.</item>
/// </list>
/// </remarks>
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
