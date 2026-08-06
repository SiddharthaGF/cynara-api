using Cynara.Domain.Capabilities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF entity configuration for <see cref="CapabilityAssignment"/>. The
/// <c>(HospitalId, ActorId, Capability)</c> composite index is unique, which
/// both enforces the one-grant-per-actor-per-capability rule and makes the
/// resolution lookup a single index seek. Every lookup filters on
/// <see cref="CapabilityAssignment.HospitalId"/> so an assignment can never
/// resolve outside its tenant.
/// </summary>
public sealed class CapabilityAssignmentEntityConfiguration
    : IEntityTypeConfiguration<CapabilityAssignment>
{
    public void Configure(EntityTypeBuilder<CapabilityAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("capability_assignments");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.ActorId,
            item.Capability,
        }).IsUnique();
        _ = builder.Property(item => item.ActorId)
            .HasMaxLength(128)
            .IsRequired();
        _ = builder.Property(item => item.Capability)
            .HasMaxLength(64)
            .IsRequired();
        _ = builder.Property(item => item.AssignedBy)
            .HasMaxLength(128);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasQueryFilter(_ => true);
    }
}
