namespace Cynara.Application.Modules.Patients;

/// <summary>
/// Tenant-aware lifecycle service for the patient registry.
/// Implementations stamp ownership from <c>IHospitalContext</c> so clients
/// cannot move records between tenants, enforce hospital-scoped MRN
/// uniqueness, honor concurrency, and emit audit events in the boundary.
/// </summary>
public interface IPatientService
{
    /// <summary>Creates a new patient under the resolved hospital workspace.</summary>
    public Task<PatientDto> CreateAsync(
        CreatePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the patient matching the supplied identifier within the
    /// resolved hospital workspace. Soft-deleted records are hidden.
    /// </summary>
    public Task<PatientDto> GetAsync(
        Guid id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Searches the roster for the resolved workspace. Soft-deleted records
    /// are hidden unless <see cref="PatientSearchRequest.IncludeDeleted"/> is
    /// true; results page via <see cref="PatientSearchRequest.Page"/> and
    /// <see cref="PatientSearchRequest.PageSize"/>.
    /// </summary>
    public Task<PatientListResponse> SearchAsync(
        PatientSearchRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Updates the mutable demographic fields on an existing patient.
    /// The MRN is immutable after creation.
    /// </summary>
    public Task<PatientDto> UpdateAsync(
        Guid id,
        UpdatePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes an existing patient. The record is hidden from
    /// default search and detail responses but remains resolvable for
    /// historical form responses and audit continuity.
    /// </summary>
    public Task<PatientDto> SoftDeleteAsync(
        Guid id,
        SoftDeletePatientRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
