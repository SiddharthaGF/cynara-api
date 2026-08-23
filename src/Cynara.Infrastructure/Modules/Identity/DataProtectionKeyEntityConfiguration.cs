using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for the DataProtection key ring table. The
/// ring persists in the identity database so OpenIddict refresh tokens
/// and authorization artifacts survive restarts, deploys, and scaled
/// instances; a friendly-name index bounds lookup cost when the ring
/// grows through key rotation.
/// </summary>
public sealed class DataProtectionKeyEntityConfiguration
    : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("data_protection_keys");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.FriendlyName)
            .HasMaxLength(256);
        _ = builder.Property(item => item.Xml)
            .IsRequired();
        _ = builder.HasIndex(item => item.FriendlyName);
    }
}
