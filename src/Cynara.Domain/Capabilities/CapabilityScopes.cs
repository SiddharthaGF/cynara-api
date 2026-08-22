namespace Cynara.Domain.Capabilities;

/// <summary>
/// Scope dimension of a capability assignment. A <see cref="Hospital"/> grant
/// authorizes its capability only inside the assigned hospital; a
/// <see cref="Platform"/> grant authorizes it in every hospital context.
/// Scope breadth lives exclusively on the grant row — capability codes never
/// encode it. Platform rows keep the issuing hospital in
/// <see cref="CapabilityAssignment.HospitalId"/> for traceability; that value
/// is authorization-irrelevant for platform scope.
/// </summary>
public static class CapabilityScopes
{
    public const string Hospital = "hospital";

    public const string Platform = "platform";
}
