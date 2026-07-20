using Cynara.Application.Persistence;
using Cynara.Domain.Audit;
using Cynara.Domain.Components;
using Cynara.Domain.Forms;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Persistence;

public sealed class CynaraDbContext(DbContextOptions<CynaraDbContext> options) : DbContext(options)
{
    public DbSet<ComponentDefinition> ComponentDefinitions => Set<ComponentDefinition>();

    public DbSet<ComponentVersion> ComponentVersions => Set<ComponentVersion>();

    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();

    public DbSet<FormVersion> FormVersions => Set<FormVersion>();

    public DbSet<FormResponse> FormResponses => Set<FormResponse>();

    public DbSet<FormResponseRevision> FormResponseRevisions => Set<FormResponseRevision>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<ComponentDefinition>(entity =>
        {
            _ = entity.ToTable("component_definitions");
            _ = entity.HasKey(item => item.Id);
            _ = entity.HasIndex(item => item.Code).IsUnique();
            _ = entity.Property(item => item.Code).HasMaxLength(128).IsRequired();
            _ = entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
            _ = entity.HasQueryFilter(item => item.DeletedAt == null);
        });

        _ = modelBuilder.Entity<ComponentVersion>(entity =>
        {
            _ = entity.ToTable("component_versions");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.Version).HasMaxLength(32);
            _ = entity.Property(item => item.ClinicalSchemaJson).IsRequired();
            _ = entity.Property(item => item.ContentHash).HasMaxLength(64);
            _ = entity.Property(item => item.RowVersion).IsConcurrencyToken();
            _ = entity.HasIndex(item => new { item.ComponentDefinitionId, item.Version }).IsUnique();
            _ = entity.HasOne(item => item.ComponentDefinition)
                .WithMany(item => item.Versions)
                .HasForeignKey(item => item.ComponentDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity.HasQueryFilter(item => item.ComponentDefinition.DeletedAt == null);
        });

        _ = modelBuilder.Entity<FormDefinition>(entity =>
        {
            _ = entity.ToTable("form_definitions");
            _ = entity.HasKey(item => item.Id);
            _ = entity.HasIndex(item => item.Code).IsUnique();
            _ = entity.Property(item => item.Code).HasMaxLength(128).IsRequired();
            _ = entity.Property(item => item.Name).HasMaxLength(256).IsRequired();
            _ = entity.HasQueryFilter(item => item.DeletedAt == null);
        });

        _ = modelBuilder.Entity<FormVersion>(entity =>
        {
            _ = entity.ToTable("form_versions");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.Version).HasMaxLength(32);
            _ = entity.Property(item => item.ClinicalSchemaJson).IsRequired();
            _ = entity.Property(item => item.ContentHash).HasMaxLength(64);
            _ = entity.Property(item => item.DependencyMetadataJson);
            _ = entity.Property(item => item.PublishedSchemaVersion).HasMaxLength(32);
            _ = entity.Property(item => item.LastReviewComment);
            _ = entity.Property(item => item.LastReviewDecision).HasMaxLength(32);
            _ = entity.Property(item => item.RowVersion).IsConcurrencyToken();
            _ = entity.HasIndex(item => new { item.FormDefinitionId, item.Version }).IsUnique();
            _ = entity.HasOne(item => item.FormDefinition)
                .WithMany(item => item.Versions)
                .HasForeignKey(item => item.FormDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity.HasQueryFilter(item => item.FormDefinition.DeletedAt == null);
        });

        _ = modelBuilder.Entity<FormResponse>(entity =>
        {
            _ = entity.ToTable("form_responses");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.AnswersJson).IsRequired();
            _ = entity.Property(item => item.RowVersion).IsConcurrencyToken();
            _ = entity.HasOne(item => item.FormVersion)
                .WithMany()
                .HasForeignKey(item => item.FormVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity.HasQueryFilter(item => item.DeletedAt == null);
        });

        _ = modelBuilder.Entity<FormResponseRevision>(entity =>
        {
            _ = entity.ToTable("form_response_revisions");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.AnswersJson).IsRequired();
            _ = entity.Property(item => item.ActorId).HasMaxLength(128);
            _ = entity.HasIndex(item => new { item.FormResponseId, item.RevisionNumber }).IsUnique();
            _ = entity.HasOne(item => item.FormResponse)
                .WithMany(item => item.Revisions)
                .HasForeignKey(item => item.FormResponseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<AuditEvent>(entity =>
        {
            _ = entity.ToTable("audit_events");
            _ = entity.HasKey(item => item.Id);
            _ = entity.Property(item => item.ResourceType).HasMaxLength(64).IsRequired();
            _ = entity.Property(item => item.Action).HasMaxLength(64).IsRequired();
            _ = entity.Property(item => item.ActorId).HasMaxLength(128);
            _ = entity.HasIndex(item => new { item.ResourceType, item.ResourceId });
            _ = entity.HasIndex(item => item.ActorId);
            _ = entity.HasIndex(item => item.OccurredAt);
        });
    }
}
