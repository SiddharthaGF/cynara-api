using Microsoft.AspNetCore.Identity;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for the <see cref="Membership"/> bridge; the
/// unique (UserId, HospitalId) index enforces one membership per hospital
/// and ActorId width matches capability assignments.
/// </summary>
public sealed class MembershipEntityConfiguration
    : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("memberships");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.UserId).IsRequired();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => new { item.UserId, item.HospitalId })
            .IsUnique();
        _ = builder.Property(item => item.ActorId)
            .HasMaxLength(128)
            .IsRequired();
        _ = builder.Property(item => item.CreatedAt).IsRequired();
        _ = builder.HasOne<IdentityUser<Guid>>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
