using Cynara.Application.Common;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Clinical-area lifecycle for <see cref="IClinicalTaxonomyService"/>.
/// Split into a partial file so that each lifecycle file stays under the
/// 400-line <c>S104</c> SonarQube profile budget.
/// </summary>
public sealed partial class ClinicalTaxonomyService
{
    public async Task<IReadOnlyList<ClinicalAreaDto>> ListClinicalAreasAsync(
        Guid? facilityId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        IReadOnlyList<ClinicalArea> areas = await repository
            .ListClinicalAreasAsync(
                hospitalContext.HospitalId,
                facilityId,
                includeRetired,
                cancellationToken)
            .ConfigureAwait(false);
        return [.. areas.Select(ClinicalTaxonomyMappers.ToDto)];
    }

    public async Task<ClinicalAreaDto> CreateClinicalAreaAsync(
        CreateClinicalAreaRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        ClinicalTaxonomyWorkflowHelpers.EnsureValidCode(request.Code, "Clinical area");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Clinical area name is required.");
        }

        Facility parent = await repository
            .FindFacilityByIdAsync(
                hospitalContext.HospitalId,
                request.FacilityId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Facility '{request.FacilityId}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureParentActive(
            parent.Status,
            "Facility",
            parent.Code,
            "clinical areas");

        if (await repository.ClinicalAreaCodeExistsAsync(
                hospitalContext.HospitalId,
                request.Code,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Clinical area '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        ClinicalArea clinicalArea = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            FacilityId = parent.Id,
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.ClinicalArea,
            clinicalArea.Id,
            "clinical-area.created",
            actorId,
            now,
            new
            {
                code = clinicalArea.Code,
                facilityId = parent.Id,
                facilityCode = parent.Code,
            });

        repository.AddClinicalArea(clinicalArea);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(clinicalArea);
    }
}
