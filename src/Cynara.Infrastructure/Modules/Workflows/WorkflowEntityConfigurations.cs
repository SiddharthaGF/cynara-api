using Cynara.Domain.Workflows;

namespace Cynara.Infrastructure.Modules.Workflows;

public sealed class WorkflowDefinitionConfiguration
    : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("workflow_definitions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.HasIndex(item => new { item.HospitalId, item.Code }).IsUnique();
        _ = builder.Property(item => item.Code).HasMaxLength(128).IsRequired();
        _ = builder.Property(item => item.Name).HasMaxLength(256).IsRequired();
        _ = builder.HasQueryFilter(item => item.DeletedAt == null);
    }
}

public sealed class WorkflowVersionConfiguration
    : IEntityTypeConfiguration<WorkflowVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.ToTable("workflow_versions");
        _ = builder.HasKey(item => item.Id);
        _ = builder.Property(item => item.HospitalId).IsRequired();
        _ = builder.HasIndex(item => item.HospitalId);
        _ = builder.Property(item => item.Version).HasMaxLength(32);
        _ = builder.Property(item => item.WorkflowSchemaJson).IsRequired();
        _ = builder.Property(item => item.ContentHash).HasMaxLength(64);
        _ = builder.Property(item => item.RowVersion).IsConcurrencyToken();
        _ = builder.HasIndex(item => new
        {
            item.HospitalId,
            item.WorkflowDefinitionId,
            item.Version,
        }).IsUnique();
        _ = builder.HasOne(item => item.WorkflowDefinition)
            .WithMany(item => item.Versions)
            .HasForeignKey(item => item.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        _ = builder.HasQueryFilter(item =>
            item.HospitalId == item.WorkflowDefinition.HospitalId
            && item.WorkflowDefinition.DeletedAt == null);
    }
}
