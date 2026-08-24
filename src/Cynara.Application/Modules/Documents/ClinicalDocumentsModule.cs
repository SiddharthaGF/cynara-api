namespace Cynara.Application.Modules.Documents;

public static class ClinicalDocumentsModule
{
    public static IServiceCollection AddClinicalDocumentsModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IClinicalDocumentService, ClinicalDocumentService>();
        _ = services.AddScoped<
            IClinicalDocumentReferenceResolver,
            ClinicalDocumentReferenceResolver>();
        _ = services.AddScoped<
            IClinicalDocumentResponseStage,
            ClinicalDocumentResponseStage>();
        return services;
    }
}
