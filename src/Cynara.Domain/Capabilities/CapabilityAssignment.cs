using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Capabilities;

/// <summary>
/// A capability granted to an actor: hospital-scoped grants authorize only
/// inside <see cref="HospitalId"/>, platform-scoped grants everywhere.
/// Resolution unions both scopes; its hospital leg always filters on
/// <see cref="HospitalId"/> first, so tenants can never cross-authorize.
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
