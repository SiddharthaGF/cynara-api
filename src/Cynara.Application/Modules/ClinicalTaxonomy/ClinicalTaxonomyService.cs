using Cynara.Application.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.ClinicalTaxonomy.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;

namespace Cynara.Application.Modules.ClinicalTaxonomy;

/// <summary>
/// Default implementation of <see cref="IClinicalTaxonomyService"/>.
/// All write operations stamp ownership from the resolved hospital context,
/// require a valid <see cref="ClinicalTaxonomyStatus"/> transition, and emit
/// audit events that commit in the same unit-of-work transaction.
/// </summary>
public sealed partial class ClinicalTaxonomyService(
    IClinicalTaxonomyRepository repository,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IClinicalTaxonomyService
{
    public async Task<IReadOnlyList<FacilityDto>> ListFacilitiesAsync(
        bool includeRetired,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogRead, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<Facility> facilities = await repository
            .ListFacilitiesAsync(
                hospitalContext.HospitalId,
                includeRetired,
                cancellationToken)
            .ConfigureAwait(false);
        return [.. facilities.Select(ClinicalTaxonomyMappers.ToDto)];
    }

    public async Task<FacilityDto> CreateFacilityAsync(
        CreateFacilityRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.CatalogWrite, cancellationToken)
            .ConfigureAwait(false);
        ClinicalTaxonomyWorkflowHelpers.EnsureValidCode(request.Code, "Facility");
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Facility name is required.");
        }

        if (await repository.FacilityCodeExistsAsync(
                hospitalContext.HospitalId,
                request.Code,
                cancellationToken)
            .ConfigureAwait(false))
        {
            throw new ConflictException(
                $"Facility '{request.Code}' already exists.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        Facility facility = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            Status = ClinicalTaxonomyStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        auditWriter.Append(
            AuditEntityTypes.Facility,
            facility.Id,
            "facility.created",
            actorId,
            now,
            new
            {
                code = facility.Code,
            });

        repository.AddFacility(facility);
        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return ClinicalTaxonomyMappers.ToDto(facility);
    }
}
