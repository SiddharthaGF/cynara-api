using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Clinical-area update/retire flows for <see cref="IClinicalTaxonomyService"/>.
/// Split into a partial file to keep each lifecycle file under the 400-line
/// SonarQube <c>S104</c> profile budget.
/// </summary>
public sealed partial class ClinicalTaxonomyService
{
    public async Task<ClinicalAreaDto> UpdateClinicalAreaAsync(
        Guid id,
        UpdateClinicalAreaRequest request,
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
            throw new ValidationException("Clinical area name is required.");
        }

        ClinicalArea clinicalArea = await repository
            .FindClinicalAreaByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical area '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            clinicalArea.RowVersion,
            request.RowVersion,
            "clinical area");

        DateTimeOffset now = timeProvider.GetUtcNow();
        clinicalArea.Name = request.Name.Trim();
        clinicalArea.UpdatedAt = now;
        clinicalArea.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.ClinicalArea,
            clinicalArea.Id,
            "clinical-area.updated",
            actorId,
            now,
            new
            {
                code = clinicalArea.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(clinicalArea);
    }

    public async Task<ClinicalAreaDto> RetireClinicalAreaAsync(
        Guid id,
        RetireClinicalAreaRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        ClinicalArea clinicalArea = await repository
            .FindClinicalAreaByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical area '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            clinicalArea.RowVersion,
            request.RowVersion,
            "clinical area");
        ClinicalTaxonomyWorkflowHelpers.EnsureNotRetired(
            clinicalArea.Status,
            "Clinical area",
            clinicalArea.Code);

        DateTimeOffset now = timeProvider.GetUtcNow();
        clinicalArea.Status = ClinicalTaxonomyStatus.Retired;
        clinicalArea.RetiredAt = now;
        clinicalArea.UpdatedAt = now;
        clinicalArea.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.ClinicalArea,
            clinicalArea.Id,
            "clinical-area.retired",
            actorId,
            now,
            new
            {
                code = clinicalArea.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(clinicalArea);
    }
}
