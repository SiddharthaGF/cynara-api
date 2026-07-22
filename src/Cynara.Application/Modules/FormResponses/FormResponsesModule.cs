using Cynara.Application.Forms;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.FormResponses;

public static class FormResponsesModule
{
    public static IServiceCollection AddFormResponsesModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IFormResponseValidator, FormResponseValidator>();
        _ = services.AddScoped<IFormResponseLifecycleService, FormResponseLifecycleService>();
        _ = services.AddScoped<IFormResponseQueryService, FormResponseQueriesService>();
        return services;
    }
}
