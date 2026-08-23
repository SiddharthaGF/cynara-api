namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Public membership listing shape returned by
/// <c>GET /api/me/hospitals</c>. Exposes exactly the hospital code and name —
/// never the hospital id, status, or actor identifier — so clients can build
/// a chooser without leaking tenant internals.
/// </summary>
public sealed record HospitalMembershipDto(string Code, string Name);
