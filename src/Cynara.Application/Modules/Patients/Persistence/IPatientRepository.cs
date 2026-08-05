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
    /// <c>criteria.IncludeDeleted</c> is <see langword="true"/>. Results
    /// are ordered by family name, given name, then MRN, and sliced to
    /// the requested page.
    /// </summary>
    public Task<PatientSearchPage> SearchAsync(
        Guid hospitalId,
        PatientSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Adds a new patient to the change tracker.</summary>
    public void Add(Patient patient);
}

/// <summary>
/// Filter criteria for the patient search endpoint. All fields are
/// optional; a fully empty criteria returns the active roster.
/// MRN and national ID filters match exactly. <see cref="NameTokens"/>
/// must each appear as a substring of the concatenated normalized
/// given + family name (diacritic-folded).
/// </summary>
public sealed record PatientSearchCriteria(
    string? NormalizedMrn,
    string? NormalizedNationalId,
    IReadOnlyList<string> NameTokens,
    bool IncludeDeleted);

/// <summary>
/// One page of patient search results plus the total matching count
/// before Skip/Take.
/// </summary>
public sealed record PatientSearchPage(
    IReadOnlyList<Patient> Items,
    int TotalCount);
