using Cynara.Application.Modules.FormResponses.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.FormResponses;

public static class FormResponsesPersistenceModule
{
    public static IServiceCollection AddFormResponsesPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IFormResponseRepository, FormResponseRepository>();
        return services;
    }
}
