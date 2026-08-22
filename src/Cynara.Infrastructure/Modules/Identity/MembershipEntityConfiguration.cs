using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for the <see cref="Membership"/> bridge. The
/// unique <c>(UserId, HospitalId)</c> index enforces the
/// one-membership-per-hospital rule; the actor identifier matches the
/// domain actor identity width used by capability assignments.
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
