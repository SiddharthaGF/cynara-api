using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Capabilities.Persistence;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Persistence;
using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Default capability administration workflow. Kept free of its own
/// capability gate so bootstrap and test seeding can grant the first
/// assignments; the HTTP surface that exposes this workflow is protected by
/// the endpoint-level authorization filter (capabilities.write).
/// </summary>
public sealed class CapabilityAssignmentService(
    ICapabilityAssignmentRepository repository,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    IHospitalContext hospitalContext,
    TimeProvider timeProvider) : ICapabilityAssignmentService
{
    /// <inheritdoc />
    public async Task<CapabilityAssignmentDto> GrantAsync(
        GrantCapabilityRequest request,
        string? assignedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        hospitalContext.RequireResolved();

        string actorId = RequireActorId(request.ActorId);
        string capability = RequireKnownCapability(request.Capability);

        CapabilityAssignment? existing = await repository.FindAsync(
            hospitalContext.HospitalId,
            actorId,
            capability,
            track: true,
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new ConflictException(
                $"Actor '{actorId}' already holds capability "
                + $"'{capability}'.");
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        CapabilityAssignment assignment = new()
        {
            Id = Guid.NewGuid(),
            HospitalId = hospitalContext.HospitalId,
            ActorId = actorId,
            Capability = capability,
            AssignedAt = now,
            AssignedBy = assignedBy,
        };

        repository.Add(assignment);
        auditWriter.Append(
            AuditEntityTypes.CapabilityAssignment,
            assignment.Id,
            "capability.assigned",
            assignedBy,
            now,
            new { actorId, capability });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
        return CapabilityMappers.ToDto(assignment);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string actorId,
        string capability,
        string? revokedBy,
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();

        string normalizedActorId = RequireActorId(actorId);
        string normalizedCapability = RequireKnownCapability(capability);

        CapabilityAssignment? assignment = await repository.FindAsync(
            hospitalContext.HospitalId,
            normalizedActorId,
            normalizedCapability,
            track: true,
            cancellationToken).ConfigureAwait(false) ?? throw new NotFoundException(
                $"Actor '{normalizedActorId}' does not hold capability "
                + $"'{normalizedCapability}'.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        repository.Remove(assignment);
        auditWriter.Append(
            AuditEntityTypes.CapabilityAssignment,
            assignment.Id,
            "capability.revoked",
            revokedBy,
            now,
            new { actorId = normalizedActorId, capability = normalizedCapability });

        _ = await unitOfWork.SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CapabilityAssignmentListResponse> ListAsync(
        CancellationToken cancellationToken)
    {
        hospitalContext.RequireResolved();

        IReadOnlyList<CapabilityAssignment> assignments = await repository
            .ListAsync(hospitalContext.HospitalId, cancellationToken)
            .ConfigureAwait(false);
        return new CapabilityAssignmentListResponse(
            [.. assignments.Select(CapabilityMappers.ToDto)]);
    }

    private static string RequireActorId(string actorId)
    {
        string normalized = actorId?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ValidationException(
                "actorId is required when assigning a capability.");
        }

        return normalized;
    }

    private static string RequireKnownCapability(string capability)
    {
        string normalized = capability?.Trim() ?? string.Empty;
        if (CapabilityCodes.All.All(item => !string.Equals(
                item, normalized, StringComparison.Ordinal)))
        {
            throw new ValidationException(
                $"Capability '{capability}' is not part of the Stage 2 "
                + "capability catalog.");
        }

        return normalized;
    }
}
