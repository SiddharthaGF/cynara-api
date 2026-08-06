using Cynara.Application.Common;
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
using Cynara.Application.Modules.Patients;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

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
        _ = services.AddScoped<IWorkflowContext, WorkflowContext>();
        _ = services.AddValidatorsFromAssembly(
            typeof(ApplicationServiceCollectionExtensions).Assembly);

        return services;
    }
}
