using Cynara.Domain.Common;
using Cynara.Domain.Hospitals;

namespace Cynara.Infrastructure.Modules.Hospitals;

/// <summary>
/// EF entity configuration for the <see cref="Hospital"/> aggregate. Codes
/// are unique across the platform; the surrogate identifier is the
/// <c>Id</c> Guid stamped into every tenant-owned row.
/// </summary>
public sealed class HospitalConfiguration
    : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("hospitals");
        _ = builder.HasKey(item => item.Id);
        _ = builder.HasIndex(item => item.Code).IsUnique();
        _ = builder.Property(item => item.Code).HasMaxLength(ResourceCodeRules.MaxLength).IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
    }
}
