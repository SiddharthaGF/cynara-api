using Cynara.Application.Modules.Forms.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.Forms;

public static class FormsPersistenceModule
{
    public static IServiceCollection AddFormsPersistenceModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IFormRepository, FormRepository>();
        return services;
    }
}
