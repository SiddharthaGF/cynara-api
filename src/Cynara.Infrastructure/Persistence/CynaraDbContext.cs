using Cynara.Application.Persistence;
using Cynara.Domain.Audit;
using Cynara.Domain.Capabilities;
using Cynara.Domain.ClinicalTaxonomy;
using Cynara.Domain.Components;
using Cynara.Domain.Documents;
using Cynara.Domain.Encounters;
using Cynara.Domain.Failures;
using Cynara.Domain.FormAi;
using Cynara.Domain.Forms;
using Cynara.Domain.Hospitals;
using Cynara.Domain.Invitations;
using Cynara.Domain.Patients;
using Cynara.Domain.Tasks;
using Cynara.Domain.Workflows;

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

    public DbSet<ClinicalDocument> ClinicalDocuments => Set<ClinicalDocument>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<Encounter> Encounters => Set<Encounter>();

    public DbSet<CapabilityAssignment> CapabilityAssignments => Set<CapabilityAssignment>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowVersion> WorkflowVersions => Set<WorkflowVersion>();

    public DbSet<Pipeline> WorkflowPipelines => Set<Pipeline>();

    public DbSet<PipelineHistory> WorkflowPipelineHistory => Set<PipelineHistory>();

    public DbSet<ClinicalTask> ClinicalTasks => Set<ClinicalTask>();

    /// <summary>
    /// Applies every entity configuration in this assembly except the
    /// Identity module's: auth entities belong exclusively to the dedicated
    /// identity context and must not leak into the domain model.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        _ = modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CynaraDbContext).Assembly,
            static configurationType => configurationType.Namespace?.StartsWith(
                    "Cynara.Infrastructure.Modules.Identity",
                    StringComparison.Ordinal) is not true);
    }
}
