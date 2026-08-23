namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Public read shape for a document catalog entry. Identity, references
/// (form version, facility, area, discipline), and <c>Code</c> are
/// immutable; <c>Status</c> is active or retired; <c>RowVersion</c> guards
/// optimistic concurrency for PATCH and retire.
/// </summary>
public sealed record DocumentDefinitionDto(
    Guid Id,
    string Code,
    string Name,
    string Status,
    Guid FormDefinitionId,
    Guid FormVersionId,
    Guid FacilityId,
    Guid ClinicalAreaId,
    Guid DisciplineId,
    bool AllowsMultipleInstancesPerEncounter,
    bool RequiresActorForCreation,
    bool RequiresActorForCompletion,
    uint RowVersion,
    DateTimeOffset? RetiredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create contract for document catalog entries.</summary>
public sealed record CreateDocumentDefinitionRequest(
    string Code,
    string Name,
    Guid FormVersionId,
    Guid FacilityId,
    Guid ClinicalAreaId,
    Guid DisciplineId,
    bool AllowsMultipleInstancesPerEncounter = true,
    bool RequiresActorForCreation = true,
    bool RequiresActorForCompletion = true);

/// <summary>
/// Update contract for mutable display and policy fields. Form, facility,
/// area, and discipline references cannot be moved by PATCH; clients must
/// delete and recreate to re-parent the row.
/// </summary>
public sealed record UpdateDocumentDefinitionRequest(
    string Name,
    bool AllowsMultipleInstancesPerEncounter,
    bool RequiresActorForCreation,
    bool RequiresActorForCompletion,
    uint RowVersion);

/// <summary>Retire contract for a document catalog entry.</summary>
public sealed record RetireDocumentDefinitionRequest(uint RowVersion);
