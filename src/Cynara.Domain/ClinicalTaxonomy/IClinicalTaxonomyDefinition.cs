namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Contract shared by every clinical taxonomy definition
/// (<see cref="Facility"/>, <see cref="ClinicalArea"/>,
/// <see cref="Discipline"/>). Exposes the shared identity, tenant, and
/// code shape used by the repository's generic query helpers, plus the
/// lifecycle <see cref="Status"/> for the shared active-status filter.
/// </summary>
public interface IClinicalTaxonomyDefinition
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; }

    /// <summary>Stable resource code, unique within the hospital.</summary>
    public string Code { get; }

    /// <summary>Lifecycle status of the definition.</summary>
    public ClinicalTaxonomyStatus Status { get; }
}
