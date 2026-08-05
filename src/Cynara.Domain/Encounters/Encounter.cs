namespace Cynara.Domain.Encounters;

/// <summary>
/// Tenant-scoped clinical encounter linked to a patient and organizational
/// definitions (facility and clinical area) from the same hospital.
/// Lifecycle transitions are authoritative on the application layer;
/// terminal states remain historically queryable.
/// </summary>
public sealed class Encounter
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>Patient this encounter belongs to; immutable after creation.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Facility where the encounter occurs; immutable after creation.</summary>
    public Guid FacilityId { get; set; }

    /// <summary>
    /// Clinical area (department) for the encounter; must belong to
    /// <see cref="FacilityId"/>. Immutable after creation.
    /// </summary>
    public Guid ClinicalAreaId { get; set; }

    /// <summary>Clinical classification of the encounter.</summary>
    public EncounterType Type { get; set; }

    /// <summary>
    /// Identifier of the responsible professional (actor-style string).
    /// Trimmed at write time; immutable after creation in v1.
    /// </summary>
    public required string ResponsibleProfessionalId { get; set; }

    /// <summary>Lifecycle status of the encounter.</summary>
    public EncounterStatus Status { get; set; }

    /// <summary>UTC timestamp when the encounter started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the encounter ended. Set on complete, cancel, or
    /// enter-in-error transitions; <see langword="null"/> while open.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>UTC timestamp when the encounter was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last encounter metadata change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on
    /// transitions. Mismatch returns a concurrency conflict.
    /// </summary>
    public uint RowVersion { get; set; }
}
