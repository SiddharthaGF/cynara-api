using Cynara.Domain.Forms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Forms;

public sealed class FormDefinitionConfiguration
    : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("form_definitions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code).HasMaxLength(128).IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.HasQueryFilter(item => item.DeletedAt == null);
    }
}

public sealed class FormVersionConfiguration : IEntityTypeConfiguration<FormVersion>
{
    public void Configure(EntityTypeBuilder<FormVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("form_versions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.Version).HasMaxLength(32);
        _ = builder.Property(item => item.ClinicalSchemaJson).IsRequired();
        _ = builder.Property(item => item.ContentHash).HasMaxLength(64);
        _ = builder.Property(item => item.DependencyMetadataJson);
        _ = builder.Property(item => item.PublishedSchemaVersion).HasMaxLength(32);
        _ = builder.Property(item => item.LastReviewComment);
        _ = builder.Property(item => item.LastReviewDecision).HasMaxLength(32);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.FormDefinitionId,
            item.Version,
        }).IsUnique();
        _ = builder.HasOne(item => item.FormDefinition)
            .WithMany(item => item.Versions)
            .HasForeignKey(item => item.FormDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.FormDefinition.HospitalId
            && item.FormDefinition.DeletedAt == null);
    }
}
