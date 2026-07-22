using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.FormAi;

public static class FormAiInfrastructureModule
{
    public static IServiceCollection AddFormAiInfrastructureModule(
        this IServiceCollection services)
    {
        _ = services.AddSingleton<IOpenAiConfiguration, OpenAiConfiguration>();
        _ = services.AddSingleton<IFormAiSkillLoader, FileFormAiSkillLoader>();
        _ = services.AddHttpClient<IOpenAiClient, OpenAiClient>(client =>
            client.Timeout = TimeSpan.FromMinutes(2));
        return services;
    }
}
