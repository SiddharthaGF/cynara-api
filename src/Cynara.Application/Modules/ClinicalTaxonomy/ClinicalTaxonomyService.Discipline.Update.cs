using Cynara.Application.Common;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Discipline update/retire flows for <see cref="IClinicalTaxonomyService"/>.
/// Split into a partial file so that each lifecycle file stays under the
/// 400-line <c>S104</c> SonarQube profile budget.
/// </summary>
public sealed partial class ClinicalTaxonomyService
{
    public async Task<DisciplineDto> UpdateDisciplineAsync(
        Guid id,
        UpdateDisciplineRequest request,
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
            throw new ValidationException("Discipline name is required.");
        }

        Discipline discipline = await repository
            .FindDisciplineByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Discipline '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            discipline.RowVersion,
            request.RowVersion,
            "discipline");

        DateTimeOffset now = timeProvider.GetUtcNow();
        discipline.Name = request.Name.Trim();
        discipline.UpdatedAt = now;
        discipline.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Discipline,
            discipline.Id,
            "discipline.updated",
            actorId,
            now,
            new
            {
                code = discipline.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(discipline);
    }

    public async Task<DisciplineDto> RetireDisciplineAsync(
        Guid id,
        RetireDisciplineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);

        Discipline discipline = await repository
            .FindDisciplineByIdAsync(
                hospitalContext.HospitalId,
                id,
                track: true,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Discipline '{id}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureConcurrency(
            discipline.RowVersion,
            request.RowVersion,
            "discipline");
        ClinicalTaxonomyWorkflowHelpers.EnsureNotRetired(
            discipline.Status,
            "Discipline",
            discipline.Code);

        DateTimeOffset now = timeProvider.GetUtcNow();
        discipline.Status = ClinicalTaxonomyStatus.Retired;
        discipline.RetiredAt = now;
        discipline.UpdatedAt = now;
        discipline.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Discipline,
            discipline.Id,
            "discipline.retired",
            actorId,
            now,
            new
            {
                code = discipline.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(discipline);
    }
}
