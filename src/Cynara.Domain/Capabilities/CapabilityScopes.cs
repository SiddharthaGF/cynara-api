namespace Cynara.Domain.Capabilities;

/// <summary>
/// Scope dimension of a capability assignment: hospital grants authorize
/// only inside the assigned hospital; platform grants authorize everywhere.
/// Breadth lives exclusively on the grant row — codes never encode it.
/// Platform rows keep the issuing hospital id for traceability only.
/// </summary>
public static class CapabilityScopes
{
    public const string Hospital = "hospital";

    public const string Platform = "platform";
}
