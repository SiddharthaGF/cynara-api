using Cynara.Domain.Capabilities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF entity configuration for <see cref="CapabilityAssignment"/> with
/// scope-aware partial unique indexes: (HospitalId, ActorId, Capability)
/// among hospital rows and (ActorId, Capability) among platform rows, so
/// both grant kinds coexist while lookups stay index seeks.
/// </summary>
public sealed class CapabilityAssignmentEntityConfiguration
    : IEntityTypeConfiguration<CapabilityAssignment>
{
    private const string HospitalScopeFilter =
        $"\"{nameof(CapabilityAssignment.Scope)}\" = '{CapabilityScopes.Hospital}'";

    private const string PlatformScopeFilter =
        $"\"{nameof(CapabilityAssignment.Scope)}\" = '{CapabilityScopes.Platform}'";

    public void Configure(EntityTypeBuilder<CapabilityAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("capability_assignments");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.Property(item => item.Scope)
            .HasMaxLength(16)
            .IsRequired()
            .HasDefaultValue(CapabilityScopes.Hospital);
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.ActorId,
            item.Capability,
        })
            .IsUnique()
            .HasFilter(HospitalScopeFilter);
        _ = builder.HasIndex(item => new
        {
            item.ActorId,
            item.Capability,
        })
            .IsUnique()
            .HasFilter(PlatformScopeFilter);
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
