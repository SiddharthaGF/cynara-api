namespace Cynara.Domain.Documents;

/// <summary>
/// Clinical document instance that binds the form-response engine to a
/// patient and encounter. Each instance preserves the exact published form
/// version it was started on, so historical documents keep resolving against
/// the snapshot they captured even after the catalog entry is updated or
/// retired.
/// </summary>
public sealed class ClinicalDocument
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to the catalog entry the document was started from.</summary>
    public Guid DocumentDefinitionId { get; set; }

    /// <summary>Patient the document belongs to; denormalized from the encounter.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Encounter the document is bound to; immutable after creation.</summary>
    public Guid EncounterId { get; set; }

    /// <summary>
    /// FK to the exact published form version captured at creation. Preserved
    /// through completion so historical documents remain resolvable.
    /// </summary>
    public Guid FormVersionId { get; set; }

    /// <summary>FK to the form response that carries the document's answers.</summary>
    public Guid FormResponseId { get; set; }

    /// <summary>
    /// Identifier of the actor who started the document. <see langword="null"/>
    /// when the catalog entry does not require an actor for creation.
    /// </summary>
    public string? AuthorId { get; set; }

    /// <summary>Lifecycle status of the document instance.</summary>
    public ClinicalDocumentStatus Status { get; set; }

    /// <summary>UTC timestamp when the document was started.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the document was completed;
    /// <see langword="null"/> while in progress.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>UTC timestamp of the last document metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on
    /// transitions. Mismatch returns a concurrency conflict.
    /// </summary>
    public uint RowVersion { get; set; }
}
