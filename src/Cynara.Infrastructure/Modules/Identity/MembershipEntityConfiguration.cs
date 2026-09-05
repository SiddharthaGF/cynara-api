using Cynara.Domain.Memberships;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for the <see cref="Membership"/> bridge; the
/// filtered unique indexes admit one active membership per
/// (UserId, HospitalId) and one active actor per hospital while revoked
/// history rows coexist outside the uniqueness window.
/// </summary>
public sealed class MembershipEntityConfiguration
    : IEntityTypeConfiguration<Membership>
{
    private const string ActiveOnlyFilter =
        $"\"{nameof(Membership.Status)}\" = "
        + $"'{nameof(MembershipStatus.Active)}'";

    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("memberships");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.UserId).IsRequired();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(
                item => new { item.UserId, item.HospitalId },
                "IX_memberships_UserId_HospitalId")
            .IsUnique()
            .HasFilter(ActiveOnlyFilter);
        _ = builder.HasIndex(
                item => new { item.HospitalId, item.ActorId },
                "IX_memberships_HospitalId_ActorId")
            .IsUnique()
            .HasFilter(ActiveOnlyFilter);
        _ = builder.Property(item => item.ActorId)
            .HasMaxLength(128)
            .IsRequired();
        _ = builder.Property(item => item.CreatedAt).IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(MembershipStatus.Active)
            .IsRequired();
        _ = builder.Property(item => item.ActivatedAt).IsRequired();
        _ = builder.Property(item => item.RevokedAt);
        _ = builder.Property(item => item.UpdatedAt).IsRequired();
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne<CynaraUser>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
