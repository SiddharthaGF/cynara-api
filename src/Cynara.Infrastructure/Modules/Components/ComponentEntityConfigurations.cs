using Cynara.Domain.Components;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Components;

public sealed class ComponentDefinitionConfiguration
    : IEntityTypeConfiguration<ComponentDefinition>
{
    public void Configure(EntityTypeBuilder<ComponentDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("component_definitions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code).HasMaxLength(128).IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.HasQueryFilter(item => item.DeletedAt == null);
    }
}

public sealed class ComponentVersionConfiguration
    : IEntityTypeConfiguration<ComponentVersion>
{
    public void Configure(EntityTypeBuilder<ComponentVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("component_versions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.Version).HasMaxLength(32);
        _ = builder.Property(item => item.ClinicalSchemaJson).IsRequired();
        _ = builder.Property(item => item.ContentHash).HasMaxLength(64);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.ComponentDefinitionId,
            item.Version,
        }).IsUnique();
        _ = builder.HasOne(item => item.ComponentDefinition)
            .WithMany(item => item.Versions)
            .HasForeignKey(item => item.ComponentDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.ComponentDefinition.HospitalId
            && item.ComponentDefinition.DeletedAt == null);
    }
}
