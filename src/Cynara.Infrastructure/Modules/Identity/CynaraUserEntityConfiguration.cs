namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// EF entity configuration for <see cref="CynaraUser"/>; only the
/// application-owned name columns are declared here, Identity owns the
/// rest of the AspNetUsers shape.
/// </summary>
public sealed class CynaraUserEntityConfiguration
    : IEntityTypeConfiguration<CynaraUser>
{
    public void Configure(EntityTypeBuilder<CynaraUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Property(item => item.GivenName).HasMaxLength(128);
        _ = builder.Property(item => item.FamilyName).HasMaxLength(128);
    }
}
