using Cynara.Application.Common;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Discipline lifecycle for <see cref="IClinicalTaxonomyService"/>.
/// Split into a partial file so that each lifecycle file stays under the
/// 400-line <c>S104</c> SonarQube profile budget.
/// </summary>
public sealed partial class ClinicalTaxonomyService
{
    public async Task<IReadOnlyList<DisciplineDto>> ListDisciplinesAsync(
        Guid? clinicalAreaId,
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<Discipline> disciplines = await repository
            .ListDisciplinesAsync(
                hospitalContext.HospitalId,
                clinicalAreaId,
                includeRetired,
                cancellationToken)
            .ConfigureAwait(false);
        return [.. disciplines.Select(ClinicalTaxonomyMappers.ToDto)];
    }

    public async Task<DisciplineDto> CreateDisciplineAsync(
        CreateDisciplineRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        ClinicalTaxonomyWorkflowHelpers.EnsureValidCode(request.Code, "Discipline");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Discipline name is required.");
        }

        ClinicalArea parent = await repository
            .FindClinicalAreaByIdAsync(
                hospitalContext.HospitalId,
                request.ClinicalAreaId,
                track: false,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Clinical area '{request.ClinicalAreaId}' was not found.");

        ClinicalTaxonomyWorkflowHelpers.EnsureParentActive(
            parent.Status,
            "Clinical area",
            parent.Code,
            "disciplines");

        if (await repository.DisciplineCodeExistsAsync(
                hospitalContext.HospitalId,
                request.Code,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Discipline '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Discipline discipline = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            ClinicalAreaId = parent.Id,
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.Discipline,
            discipline.Id,
            "discipline.created",
            actorId,
            now,
            new
            {
                code = discipline.Code,
                clinicalAreaId = parent.Id,
                clinicalAreaCode = parent.Code,
            });

        repository.AddDiscipline(discipline);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(discipline);
    }
}
