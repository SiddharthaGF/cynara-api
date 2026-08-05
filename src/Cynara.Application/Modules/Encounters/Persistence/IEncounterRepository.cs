using Cynara.Domain.Encounters;

namespace Cynara.Application.Modules.Encounters.Persistence;

/// <summary>
/// Persistence port for clinical encounters. All read paths are
/// hospital-scoped; write operations return tracked entities the
/// workflows can mutate without committing. The unit-of-work boundary is
/// owned by the workflow, not by the repository.
/// </summary>
public interface IEncounterRepository
{
    /// <summary>
    /// Returns the encounter matching the supplied identifier in the
    /// resolved hospital workspace, or <see langword="null"/> when no
    /// record exists. Terminal states remain resolvable.
    /// </summary>
    public Task<Encounter?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists encounters in the resolved hospital workspace that match the
    /// supplied filter. Terminal states are included by default.
    /// </summary>
    public Task<IReadOnlyList<Encounter>> ListAsync(
        Guid hospitalId,
        EncounterListCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>Adds a new encounter to the change tracker.</summary>
    public void Add(Encounter encounter);
}

/// <summary>
/// Filter criteria for the encounter list endpoint. All fields are
/// optional; a fully empty criteria returns the hospital roster.
/// </summary>
public sealed record EncounterListCriteria(
    Guid? PatientId,
    Guid? FacilityId,
    Guid? ClinicalAreaId,
    EncounterStatus? Status);
