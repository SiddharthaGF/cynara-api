using Cynara.Domain.Patients;

namespace Cynara.Application.Modules.Patients.Persistence;

/// <summary>
/// Persistence port for the patient registry. All read paths are
/// hospital-scoped; write operations return tracked entities the
/// workflows can mutate without committing. The unit-of-work boundary is
/// owned by the workflow, not by the repository.
/// </summary>
public interface IPatientRepository
{
    /// <summary>
    /// Returns the patient matching the supplied identifier in the
    /// resolved hospital workspace, or <see langword="null"/> when no
    /// record exists. Soft-deleted records are returned regardless of the
    /// caller's intent so workflows can decide whether to surface them.
    /// </summary>
    public Task<Patient?> FindByIdAsync(
        Guid hospitalId,
        Guid id,
        bool track,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the patient with the supplied normalized MRN in the
    /// resolved hospital workspace, or <see langword="null"/> when no
    /// record exists.
    /// </summary>
    public Task<Patient?> FindByNormalizedMrnAsync(
        Guid hospitalId,
        string normalizedMrn,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists patients in the resolved hospital workspace that match the
    /// supplied filter. Soft-deleted records are excluded unless
    /// <c>criteria.IncludeDeleted</c> is <see langword="true"/>.
    /// </summary>
    public Task<IReadOnlyList<Patient>> SearchAsync(
        Guid hospitalId,
        PatientSearchCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>Adds a new patient to the change tracker.</summary>
    public void Add(Patient patient);
}

/// <summary>
/// Filter criteria for the patient search endpoint. All fields are
/// optional; a fully empty criteria returns the active roster.
/// </summary>
public sealed record PatientSearchCriteria(
    string? NormalizedMrn,
    string? NormalizedNationalId,
    string? NormalizedGivenName,
    string? NormalizedFamilyName,
    bool IncludeDeleted);
