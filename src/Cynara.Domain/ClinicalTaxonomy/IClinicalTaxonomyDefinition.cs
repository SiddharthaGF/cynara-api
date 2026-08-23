namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Contract shared by every clinical taxonomy definition (facility,
/// clinical area, discipline): the identity, tenant, and code shape used by
/// generic repository helpers plus the lifecycle status for shared filters.
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
