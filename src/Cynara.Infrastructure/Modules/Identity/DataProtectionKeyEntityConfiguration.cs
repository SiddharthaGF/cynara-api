using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for the DataProtection key ring table, persisted
/// in the identity database so refresh tokens survive restarts and scaled
/// instances; a friendly-name index bounds lookup cost.
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
