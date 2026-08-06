using System.Net;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Raised when the current actor does not hold the capability required by a
/// protected endpoint or domain operation. Maps to 403 through the shared
/// error mapping. The message stays generic so a denial never reveals whether
/// the protected resource exists; the required capability and actor are kept
/// as properties for the denied-access audit.
/// </summary>
public sealed class CapabilityForbiddenException : CynaraException
{
    public CapabilityForbiddenException(
        string capability,
        string? actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        Capability = capability;
        ActorId = actorId;
    }

    /// <summary>The capability whose absence caused the denial.</summary>
    public string Capability { get; }

    /// <summary>The actor identity of the denied request, when present.</summary>
    public string? ActorId { get; }

    public override HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public override string Title => "Capability required";
}
