using Cynara.Domain.Workflows;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cynara.Infrastructure.Modules.Workflows;

/// <summary>
/// EF entity configuration for the <see cref="Pipeline"/> aggregate;
/// indexes support hospital-scoped filters and the restricted version link
/// pins a published version against deletion.
/// </summary>
public sealed class PipelineConfiguration
    : IEntityTypeConfiguration<Pipeline>
{
    public void Configure(EntityTypeBuilder<Pipeline> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("workflow_pipelines");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.Id).ValueGeneratedNever();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.SubjectType });
        _ = builder.HasIndex(item => new { item.HospitalId, item.SubjectId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.Status });
        _ = builder.HasIndex(item => new { item.HospitalId, item.WorkflowVersionId });
        _ = builder.Property(item => item.WorkflowVersionId).IsRequired();
        _ = builder.Property(item => item.SubjectType)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.SubjectId).IsRequired();
        _ = builder.Property(item => item.PatientId).IsRequired();
        _ = builder.HasIndex(item => new { item.HospitalId, item.PatientId });
        _ = builder.Property(item => item.EncounterId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.EncounterId });
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.CurrentNodeId)
            .HasMaxLength(64)
            .IsRequired();
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne(item => item.WorkflowVersion)
            .WithMany()
            .HasForeignKey(item => item.WorkflowVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.WorkflowVersion.HospitalId);
    }
}

/// <summary>
/// EF entity configuration for the append-only <see cref="PipelineHistory"/>
/// log. The unique (pipeline, sequence) index makes gaps and rewrites
/// impossible; rows are never updated or deleted by application code.
/// </summary>
public sealed class PipelineHistoryConfiguration
    : IEntityTypeConfiguration<PipelineHistory>
{
    public void Configure(EntityTypeBuilder<PipelineHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("workflow_pipeline_history");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.Id).ValueGeneratedNever();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.PipelineId).IsRequired();
        _ = builder.HasIndex(item => new
        {
            item.PipelineId,
            item.Sequence,
        }).IsUnique();
        _ = builder.Property(item => item.Action)
            .HasMaxLength(64)
            .IsRequired();
        _ = builder.Property(item => item.ActorId).HasMaxLength(128);
        _ = builder.Property(item => item.MetadataJson);
        _ = builder.HasOne(item => item.Pipeline)
            .WithMany(item => item.History)
            .HasForeignKey(item => item.PipelineId)
            .OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.Pipeline.HospitalId);
    }
}
