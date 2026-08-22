namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Request body for granting a capability to an actor. The optional scope
/// selects the grant breadth: <c>hospital</c> (default) binds the grant to
/// the resolved hospital workspace, <c>platform</c> authorizes the
/// capability in every hospital context.
/// </summary>
public sealed record GrantCapabilityRequest(
    string ActorId,
    string Capability,
    string? Scope = null);

/// <summary>
/// One persisted capability assignment for the resolved hospital workspace.
/// </summary>
public sealed record CapabilityAssignmentDto(
    Guid Id,
    string ActorId,
    string Capability,
    DateTimeOffset AssignedAt,
    string? AssignedBy,
    uint RowVersion);

/// <summary>Paged/plain list of capability assignments.</summary>
public sealed record CapabilityAssignmentListResponse(
    IReadOnlyList<CapabilityAssignmentDto> Items);

/// <summary>
/// Effective capability set for the current actor within the resolved
/// hospital workspace. Returned by <c>GET /api/me/capabilities</c> so clients
/// can drive UI affordances from the same source the API enforces.
/// </summary>
public sealed record MeCapabilitiesResponse(
    string? ActorId,
    IReadOnlyList<string> Capabilities);
