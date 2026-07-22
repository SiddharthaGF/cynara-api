using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Application.Modules.FormAi;

public static class FormAiModule
{
    public static IServiceCollection AddFormAiModule(
        this IServiceCollection services)
    {
        _ = services.AddScoped<IAiProviderSettingsService, AiProviderSettingsService>();
        _ = services.AddScoped<IFormAiService, FormAiService>();
        return services;
    }
}
