using Cynara.Application.Common;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Facility update/retire flows for <see cref="IClinicalTaxonomyService"/>.
/// Split into a partial file so that each lifecycle file stays under the
/// 400-line <c>S104</c> SonarQube profile budget.
/// </summary>
public sealed partial class ClinicalTaxonomyService
{
    public async Task<FacilityDto> UpdateFacilityAsync(
        Guid id,
        UpdateFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Facility name is required.");
        }

        Facility facility = await repository
            .FindFacilityByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Facility '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            facility.RowVersion,
            request.RowVersion,
            "facility");

        DateTimeOffset now = timeProvider.GetUtcNow();
        facility.Name = request.Name.Trim();
        facility.UpdatedAt = now;
        facility.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Facility,
            facility.Id,
            "facility.updated",
            actorId,
            now,
            new
            {
                code = facility.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(facility);
    }

    public async Task<FacilityDto> RetireFacilityAsync(
        Guid id,
        RetireFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        Facility facility = await repository
            .FindFacilityByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Facility '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            facility.RowVersion,
            request.RowVersion,
            "facility");
        ClinicalTaxonomyWorkflowHelpers.EnsureNotRetired(
            facility.Status,
            "Facility",
            facility.Code);

        DateTimeOffset now = timeProvider.GetUtcNow();
        facility.Status = ClinicalTaxonomyStatus.Retired;
        facility.RetiredAt = now;
        facility.UpdatedAt = now;
        facility.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Facility,
            facility.Id,
            "facility.retired",
            actorId,
            now,
            new
            {
                code = facility.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(facility);
    }
}
