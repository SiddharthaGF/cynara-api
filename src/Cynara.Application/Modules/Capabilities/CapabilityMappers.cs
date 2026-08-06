using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Capabilities;

/// <summary>
/// Maps <see cref="CapabilityAssignment"/> entities to the wire DTO.
/// </summary>
internal static class CapabilityMappers
{
    public static CapabilityAssignmentDto ToDto(CapabilityAssignment entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new CapabilityAssignmentDto(
            Id: entity.Id,
            ActorId: entity.ActorId,
            Capability: entity.Capability,
            AssignedAt: entity.AssignedAt,
            AssignedBy: entity.AssignedBy,
            RowVersion: entity.RowVersion);
    }
}
