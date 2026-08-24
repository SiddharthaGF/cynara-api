using Cynara.Domain.Tasks;

namespace Cynara.Infrastructure.Modules.Tasks;

/// <summary>
/// EF entity configuration for the <see cref="ClinicalTask"/> aggregate;
/// indexes support hospital-scoped filters and the restricted pipeline link
/// prevents deleting a terminating pipeline under its tasks.
/// </summary>
public sealed class ClinicalTaskConfiguration
    : IEntityTypeConfiguration<ClinicalTask>
{
    public void Configure(EntityTypeBuilder<ClinicalTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("clinical_tasks");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.Id).ValueGeneratedNever();
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Status });
        _ = builder.HasIndex(item => new { item.HospitalId, item.PatientId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.EncounterId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.PipelineId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.WorkflowDefinitionId });
        _ = builder.HasIndex(item => new { item.HospitalId, item.EncounterId, item.Status });
        _ = builder.Property(item => item.PipelineId).IsRequired();
        _ = builder.Property(item => item.WorkflowVersionId).IsRequired();
        _ = builder.Property(item => item.WorkflowDefinitionId).IsRequired();
        _ = builder.Property(item => item.NodeId)
            .HasMaxLength(64)
            .IsRequired();
        _ = builder.Property(item => item.Name)
            .HasMaxLength(256)
            .IsRequired();
        _ = builder.Property(item => item.Description).HasMaxLength(2000);
        _ = builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32);
        _ = builder.Property(item => item.AssignedActor).HasMaxLength(128);
        _ = builder.Property(item => item.AssignedRole).HasMaxLength(128);
        _ = builder.Property(item => item.AssignedDiscipline).HasMaxLength(128);
        _ = builder.Property(item => item.PatientId).IsRequired();
        _ = builder.Property(item => item.EncounterId);
        _ = builder.Property(item => item.FormCode).HasMaxLength(128);
        _ = builder.Property(item => item.FormVersion).HasMaxLength(32);
        _ = builder.Property(item => item.DueAt);
        _ = builder.Property(item => item.ClaimedBy).HasMaxLength(128);
        _ = builder.Property(item => item.CompletedBy).HasMaxLength(128);
        _ = builder.Property(item => item.CanceledBy).HasMaxLength(128);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasOne(item => item.Pipeline)
            .WithMany()
            .HasForeignKey(item => item.PipelineId)
            .OnDelete(DeleteBehavior.Restrict);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.Pipeline.HospitalId);
    }
}
