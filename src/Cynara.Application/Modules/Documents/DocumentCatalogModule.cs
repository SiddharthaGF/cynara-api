namespace Cynara.Application.Modules.Documents;

public static class DocumentCatalogModule
{
    public static IServiceCollection AddDocumentCatalogModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IDocumentCatalogService, DocumentCatalogService>();
        return services;
    }
}
