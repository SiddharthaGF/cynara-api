using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Common;

namespace Cynara.Infrastructure.Modules.ClinicalTaxonomy;

/// <summary>
/// EF entity configuration for the <see cref="Facility"/> aggregate.
/// Codes are unique within the hospital workspace; the surrogate
/// <c>Id</c> drives relationships and foreign keys.
/// </summary>
public sealed class FacilityConfiguration
    : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("facilities");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code)
            .HasMaxLength(ResourceCodeRules.MaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasQueryFilter(_ => true);
        _ = builder.HasMany(item => item.ClinicalAreas)
            .WithOne(item => item.Facility)
            .HasForeignKey(item => item.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// EF entity configuration for the <see cref="ClinicalArea"/> aggregate.
/// Codes are unique within the hospital workspace; the surrogate
/// <c>Id</c> drives relationships and foreign keys.
/// </summary>
public sealed class ClinicalAreaConfiguration
    : IEntityTypeConfiguration<ClinicalArea>
{
    public void Configure(EntityTypeBuilder<ClinicalArea> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("clinical_areas");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => item.FacilityId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code)
            .HasMaxLength(ResourceCodeRules.MaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne(item => item.Facility)
            .WithMany(item => item.ClinicalAreas)
            .HasForeignKey(item => item.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.Facility.HospitalId);
        _ = builder.HasMany(item => item.Disciplines)
            .WithOne(item => item.ClinicalArea)
            .HasForeignKey(item => item.ClinicalAreaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// EF entity configuration for the <see cref="Discipline"/> aggregate.
/// Codes are unique within the hospital workspace; the surrogate
/// <c>Id</c> drives relationships and foreign keys.
/// </summary>
public sealed class DisciplineConfiguration
    : IEntityTypeConfiguration<Discipline>
{
    public void Configure(EntityTypeBuilder<Discipline> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("disciplines");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => item.ClinicalAreaId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code)
            .HasMaxLength(ResourceCodeRules.MaxLength)
            .IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne(item => item.ClinicalArea)
            .WithMany(item => item.Disciplines)
            .HasForeignKey(item => item.ClinicalAreaId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.ClinicalArea.HospitalId);
    }
}
