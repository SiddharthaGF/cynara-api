namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Public membership listing shape returned by
/// <c>GET /api/me/hospitals</c>. Exposes exactly the hospital <c>code</c> and
/// <c>name</c>; never the hospital id, status, or the user's actor identifier,
/// so clients can build a hospital chooser without leaking tenant or
/// membership internals.
/// </summary>
public sealed record HospitalMembershipDto(string Code, string Name);
