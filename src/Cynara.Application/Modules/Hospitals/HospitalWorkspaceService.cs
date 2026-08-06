using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Default implementation of <see cref="IHospitalWorkspaceService"/>.
/// All write operations stamp ownership from the resolved context so
/// clients cannot move the workspace between tenants.
/// </summary>
public sealed class HospitalWorkspaceService(
    IHospitalContext hospitalContext,
    IHospitalRepository hospitals,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    TimeProvider timeProvider,
    ICapabilityGuard capabilityGuard) : IHospitalWorkspaceService
{
    public async Task<HospitalWorkspaceDto> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkspaceRead, cancellationToken)
            .ConfigureAwait(false);
        Domain.Hospitals.Hospital hospital = await hospitals
            .FindByIdAsync(hospitalContext.HospitalId, track: false, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Hospital '{hospitalContext.Code}' was not found.");
        return HospitalMappers.ToDto(hospital);
    }

    public async Task<HospitalWorkspaceDto> UpdateCurrentAsync(
        UpdateHospitalWorkspaceRequest request,
        string? actorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();
        await capabilityGuard.RequireAsync(
            CapabilityCodes.WorkspaceWrite, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("Hospital name is required.");
        }

        Domain.Hospitals.Hospital hospital = await hospitals
            .FindByIdAsync(hospitalContext.HospitalId, track: true, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException(
                $"Hospital '{hospitalContext.Code}' was not found.");

        if (hospital.RowVersion != request.RowVersion)
        {
            throw new ConcurrencyException(
                "The hospital workspace was modified by another request.");
        }

        hospital.Name = request.Name.Trim();
        hospital.MetadataJson = request.MetadataJson;
        hospital.UpdatedAt = timeProvider.GetUtcNow();
        hospital.RowVersion = request.RowVersion + 1;

        auditWriter.Append(
            AuditEntityTypes.Hospital,
            hospital.Id,
            "hospital.updated",
            actorId,
            hospital.UpdatedAt,
            new
            {
                code = hospital.Code,
                rowVersion = request.RowVersion,
            });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return HospitalMappers.ToDto(hospital);
    }
}
