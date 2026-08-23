using Cynara.Domain.Invitations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Invitations;

/// <summary>
/// EF entity configuration for <see cref="Invitation"/>; the unique
/// token-hash index gates activation so resending instantly supersedes
/// every previously issued link.
/// </summary>
public sealed class InvitationEntityConfiguration
    : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("invitations");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.Email)
            .HasMaxLength(256)
            .IsRequired();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.Property(item => item.TokenHash)
            .HasMaxLength(64)
            .IsRequired();
        _ = builder.HasIndex(item => item.TokenHash).IsUnique();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.LinkVersion)
            .IsRequired()
            .HasDefaultValue(1);
        _ = builder.Property(item => item.Status).IsRequired();
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
    }
}
