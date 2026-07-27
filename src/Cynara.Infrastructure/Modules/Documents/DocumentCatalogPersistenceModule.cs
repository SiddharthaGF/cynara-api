using Cynara.Application.Modules.Documents.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Documents;

public static class DocumentCatalogPersistenceModule
{
    public static IServiceCollection AddDocumentCatalogPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IDocumentCatalogRepository, DocumentCatalogRepository>();
        return services;
    }
}
