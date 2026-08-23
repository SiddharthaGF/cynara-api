namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Capability administration workflow. Grants and revocations validate
/// against the capability catalog and an explicit or default scope, emit
/// audit events in the same unit-of-work transaction, and bind hospital
/// grants to the resolved tenant; platform grants authorize everywhere.
/// </summary>
public interface ICapabilityAssignmentService
{
    public Task<CapabilityAssignmentDto> GrantAsync(
        GrantCapabilityRequest request,
        string? assignedBy,
        CancellationToken cancellationToken);

    public Task RevokeAsync(
        string actorId,
        string capability,
        string? revokedBy,
        string? scope,
        CancellationToken cancellationToken);

    public Task<CapabilityAssignmentListResponse> ListAsync(
        CancellationToken cancellationToken);
}
