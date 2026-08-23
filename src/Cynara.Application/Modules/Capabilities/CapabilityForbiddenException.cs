using System.Net;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Raised when the current actor does not hold the capability required by a
/// protected endpoint or domain operation; maps to 403. The message stays
/// generic so a denial never reveals whether the resource exists — the
/// required capability and actor are properties for the denied-access audit.
/// </summary>
public sealed class CapabilityForbiddenException : CynaraException
{
    public CapabilityForbiddenException()
        : this(string.Empty)
    {
    }

    public CapabilityForbiddenException(string message)
        : base(message)
    {
    }

    public CapabilityForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CapabilityForbiddenException(
        string capability,
        string? actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        Capability = capability;
        ActorId = actorId;
    }

    /// <summary>The capability whose absence caused the denial.</summary>
    public string Capability { get; } = string.Empty;

    /// <summary>The actor identity of the denied request, when present.</summary>
    public string? ActorId { get; }

    public override HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public override string Title => "Capability required";
}
