using Cynara.Application.Modules.Audit;
using Cynara.Application.Modules.Capabilities;
using Cynara.Application.Modules.ClinicalTaxonomy;
using Cynara.Application.Modules.Components;
using Cynara.Application.Modules.Documents;
using Cynara.Application.Modules.Encounters;
using Cynara.Application.Modules.FormAi;
using Cynara.Application.Modules.FormResponses;
using Cynara.Application.Modules.Forms;
using Cynara.Application.Modules.Hospitals;
using Cynara.Application.Modules.Invitations;
using Cynara.Application.Modules.Memberships;
using Cynara.Application.Modules.Patients;
using Cynara.Application.Modules.Tasks;
using Cynara.Application.Modules.Users;
using Cynara.Application.Modules.Workflows;

namespace Cynara.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraApplication(
        this IServiceCollection services)
    {
        _ = services.AddHospitalsModule();
        _ = services.AddAuditModule();
        _ = services.AddComponentsModule();
        _ = services.AddFormsModule();
        _ = services.AddFormResponsesModule();
        _ = services.AddFormAiModule();
        _ = services.AddClinicalTaxonomyModule();
        _ = services.AddDocumentCatalogModule();
        _ = services.AddClinicalDocumentsModule();
        _ = services.AddPatientsModule();
        _ = services.AddEncountersModule();
        _ = services.AddCapabilitiesModule();
        _ = services.AddUsersModule();
        _ = services.AddScoped<InvitationAdminWorkflow>();
        _ = services.AddScoped<InvitationAcceptancePersistence>();
        _ = services.AddScoped<InvitationAcceptanceContext>();
        _ = services.AddScoped<InvitationAcceptanceWorkflow>();
        _ = services.AddScoped<MembershipAdminWorkflow>();
        _ = services.AddWorkflowsModule();
        _ = services.AddTasksModule();
        _ = services.AddScoped<IWorkflowContext, WorkflowContext>();
        _ = services.AddScoped<TransactionalDeps>();

        return services;
    }
}
