using Cynara.Application.Forms;

namespace Cynara.Application.Modules.FormResponses;

public static class FormResponsesModule
{
    public static IServiceCollection AddFormResponsesModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IFormResponseValidator, FormResponseValidator>();
        _ = services.AddScoped<IFormResponseLifecycleService, FormResponseLifecycleService>();
        return services;
    }
}
