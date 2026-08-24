using Cynara.Application.Modules.Documents.Persistence;

namespace Cynara.Infrastructure.Modules.Documents;

/// <summary>
/// Composition extensions that wire the clinical document persistence
/// services.
/// </summary>
public static class ClinicalDocumentsPersistenceModule
{
    public static IServiceCollection AddClinicalDocumentsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IClinicalDocumentRepository, ClinicalDocumentRepository>();
        return services;
    }
}
