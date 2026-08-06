namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Capability administration workflow. Grants and revocations are
/// hospital-scoped through the resolved tenant context, validate against the
/// known capability catalog, and emit audit events that commit in the same
/// unit-of-work transaction as the assignment change.
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
        CancellationToken cancellationToken);

    public Task<CapabilityAssignmentListResponse> ListAsync(
        CancellationToken cancellationToken);
}
