namespace Cynara.Application.Modules.Documents;

/// <summary>
/// Public read and write shapes for the clinical document catalog.
/// </summary>
/// <remarks>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Code</c> – stable business code unique within the hospital; immutable.</item>
/// <item><c>Name</c> – human-readable document name; mutable.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>retired</c>.</item>
/// <item><c>FormDefinitionId</c> – owning form definition; immutable.</item>
/// <item><c>FormVersionId</c> – exact published form version; immutable
/// after creation so historical documents remain resolvable.</item>
/// <item><c>FacilityId</c> – owning facility; immutable.</item>
/// <item><c>ClinicalAreaId</c> – owning clinical area; immutable.</item>
/// <item><c>DisciplineId</c> – owning discipline; immutable.</item>
/// <item><c>AllowsMultipleInstancesPerEncounter</c> – policy flag.</item>
/// <item><c>RequiresActorForCreation</c> – capability flag for creation.</item>
/// <item><c>RequiresActorForCompletion</c> – capability flag for completion.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; required for
/// PATCH/retire.</item>
/// <item><c>RetiredAt</c> – UTC timestamp when retired; immutable after retirement.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>UpdatedAt</c> – UTC timestamp of the last metadata change.</item>
/// </list>
/// </remarks>
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
