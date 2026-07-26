namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Contract shared by every clinical taxonomy definition
/// (<see cref="Facility"/>, <see cref="ClinicalArea"/>,
/// <see cref="Discipline"/>). Exposes the lifecycle
/// <see cref="Status"/> so the repository can share a single active-status
/// filter across all three aggregates.
/// </summary>
public interface IClinicalTaxonomyDefinition
{
    /// <summary>Lifecycle status of the definition.</summary>
    public ClinicalTaxonomyStatus Status { get; }
}
