using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Capabilities;

/// <summary>
/// A capability granted to an actor within one hospital workspace. The
/// hospital-scoped composite index on <c>(HospitalId, ActorId, Capability)</c>
/// is what keeps an assignment in one tenant from ever authorizing access in
/// another: every resolution query filters on <see cref="HospitalId"/> first.
/// </summary>
[NoResource]
public sealed class CapabilityAssignment
{
    public Guid Id { get; set; }

    public Guid HospitalId { get; set; }

    public required string ActorId { get; set; }

    public required string Capability { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public string? AssignedBy { get; set; }

    public uint RowVersion { get; set; }
}
