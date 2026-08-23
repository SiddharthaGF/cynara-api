namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Tenant-aware CRUD lifecycle for the clinical taxonomy aggregates.
/// Implementations stamp ownership from
/// <see cref="Hospitals.IHospitalContext"/>, honor concurrency, keep codes
/// unique within the hospital, and preserve retired definitions for history.
/// </summary>
public interface IClinicalTaxonomyService
{
    /// <summary>Lists facility definitions for the resolved hospital.</summary>
    public Task<IReadOnlyList<FacilityDto>> ListFacilitiesAsync(
        bool includeRetired,
        CancellationToken cancellationToken);

    /// <summary>Creates a new facility under the resolved hospital.</summary>
    public Task<FacilityDto> CreateFacilityAsync(
        CreateFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Updates mutable display fields on an existing facility.</summary>
    public Task<FacilityDto> UpdateFacilityAsync(
        Guid id,
        UpdateFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Retires a facility. The definition remains resolvable for historical records.</summary>
    public Task<FacilityDto> RetireFacilityAsync(
        Guid id,
        RetireFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Lists clinical area definitions for the resolved hospital.</summary>
    public Task<IReadOnlyList<ClinicalAreaDto>> ListClinicalAreasAsync(
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken);

    /// <summary>Creates a new clinical area under the resolved hospital.</summary>
    public Task<ClinicalAreaDto> CreateClinicalAreaAsync(
        CreateClinicalAreaRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Updates mutable display fields on an existing clinical area.</summary>
    public Task<ClinicalAreaDto> UpdateClinicalAreaAsync(
        Guid id,
        UpdateClinicalAreaRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Retires a clinical area. The definition remains resolvable for historical records.</summary>
    public Task<ClinicalAreaDto> RetireClinicalAreaAsync(
        Guid id,
        RetireClinicalAreaRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Lists discipline definitions for the resolved hospital.</summary>
    public Task<IReadOnlyList<DisciplineDto>> ListDisciplinesAsync(
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken);

    /// <summary>Creates a new discipline under the resolved hospital.</summary>
    public Task<DisciplineDto> CreateDisciplineAsync(
        CreateDisciplineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Updates mutable display fields on an existing discipline.</summary>
    public Task<DisciplineDto> UpdateDisciplineAsync(
        Guid id,
        UpdateDisciplineRequest request,
        string? actorId,
        CancellationToken cancellationToken);

    /// <summary>Retires a discipline. The definition remains resolvable for historical records.</summary>
    public Task<DisciplineDto> RetireDisciplineAsync(
        Guid id,
        RetireDisciplineRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}
