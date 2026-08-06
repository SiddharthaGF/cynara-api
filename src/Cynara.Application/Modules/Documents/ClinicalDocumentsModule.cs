using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.Documents;

public static class ClinicalDocumentsModule
{
    public static IServiceCollection AddClinicalDocumentsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IClinicalDocumentService, ClinicalDocumentService>();
        return services;
    }
}
