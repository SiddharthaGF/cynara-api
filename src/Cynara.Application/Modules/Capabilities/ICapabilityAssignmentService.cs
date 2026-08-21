namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Capability administration workflow. Grants and revocations validate
/// against the known capability catalog and an explicit or default hospital
/// scope, and emit audit events that commit in the same unit-of-work
/// transaction as the assignment change. Hospital-scoped grants bind to the
/// resolved tenant context; platform-scoped grants authorize in every
/// hospital context while keeping the issuing hospital for traceability.
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
