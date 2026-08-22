namespace Cynara.Api.Common.ActorContext;

/// <summary>
/// Scoped holder for the actor identity resolved from the authenticated
/// user's hospital membership. The membership middleware populates it per
/// request from the token <c>sub</c> and the resolved hospital; the
/// <see cref="PrincipalCurrentActor"/> reads it so the Application layer
/// keeps consuming the same <c>ICurrentActor</c> abstraction. Client
/// credentials and out-of-request flows leave it empty (<see langword="null"/>),
/// which resolves to an empty capability set (deny by default).
/// </summary>
public sealed class ResolvedActor
{
    /// <summary>The actor identity resolved for the current request.</summary>
    public string? ActorId { get; set; }
}
