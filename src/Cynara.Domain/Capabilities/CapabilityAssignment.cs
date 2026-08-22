using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Capabilities;

/// <summary>
/// A capability granted to an actor. Hospital-scoped grants authorize only
/// inside <see cref="HospitalId"/>; platform-scoped grants authorize in every
/// hospital context. Resolution is the union of both scopes for the actor, so
/// an assignment in one tenant can still never authorize another through a
/// hospital grant: the hospital leg of every resolution query filters on
/// <see cref="HospitalId"/> first.
/// </summary>
[NoResource]
public sealed class CapabilityAssignment
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public required string ActorId { get; set; }

    public required string Capability { get; set; }

    /// <summary>
    /// Grant breadth: <see cref="CapabilityScopes.Hospital"/> (default) or
    /// <see cref="CapabilityScopes.Platform"/>. On platform rows
    /// <see cref="HospitalId"/> records the issuing context and is
    /// authorization-irrelevant.
    /// </summary>
    public string Scope { get; set; } = CapabilityScopes.Hospital;

    public DateTimeOffset AssignedAt { get; set; }

    public string? AssignedBy { get; set; }

    public uint RowVersion { get; set; }
}
