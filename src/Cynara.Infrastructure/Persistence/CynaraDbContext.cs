using Cynara.Application.Persistence;
using Cynara.Domain.Audit;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Components;
using Cynara.Domain.Documents;
using Cynara.Domain.Failures;
using Cynara.Domain.FormAi;
using Cynara.Domain.Forms;
using Cynara.Domain.Hospitals;

using Microsoft.EntityFrameworkCore;

namespace Cynara.Infrastructure.Persistence;

public sealed class CynaraDbContext(DbContextOptions<CynaraDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<Hospital> Hospitals => Set<Hospital>();

    public DbSet<ComponentDefinition> ComponentDefinitions => Set<ComponentDefinition>();

    public DbSet<ComponentVersion> ComponentVersions => Set<ComponentVersion>();

    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();

    public DbSet<FormVersion> FormVersions => Set<FormVersion>();

    public DbSet<FormResponse> FormResponses => Set<FormResponse>();

    public DbSet<FormResponseRevision> FormResponseRevisions => Set<FormResponseRevision>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<AiProviderSettings> AiProviderSettings => Set<AiProviderSettings>();

    public DbSet<FailureLog> FailureLogs => Set<FailureLog>();

    public DbSet<Facility> Facilities => Set<Facility>();

    public DbSet<ClinicalArea> ClinicalAreas => Set<ClinicalArea>();

    public DbSet<Discipline> Disciplines => Set<Discipline>();

    public DbSet<DocumentDefinition> DocumentDefinitions => Set<DocumentDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        _ = modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CynaraDbContext).Assembly);
    }
}
