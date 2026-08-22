using Cynara.Domain.Capabilities;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Capabilities;

/// <summary>
/// EF entity configuration for <see cref="CapabilityAssignment"/>. Uniqueness
/// is scope-aware: the <c>(HospitalId, ActorId, Capability)</c> composite is
/// unique among hospital-scoped rows (one grant per actor per capability per
/// tenant), while <c>(ActorId, Capability)</c> is unique among platform-scoped
/// rows (one platform grant per actor per capability). Both are partial
/// indexes on <see cref="CapabilityAssignment.Scope"/>, which keeps the
/// resolution lookup an index seek and lets hospital and platform grants for
/// the same capability coexist.
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
