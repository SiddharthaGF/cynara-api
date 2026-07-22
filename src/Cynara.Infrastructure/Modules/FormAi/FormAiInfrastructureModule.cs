using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cynara.Infrastructure.Modules.FormAi;

public static class FormAiInfrastructureModule
{
    public static IServiceCollection AddFormAiInfrastructureModule(
        this IServiceCollection services)
    {
        _ = services.AddSingleton<IOpenAiConfiguration, OpenAiConfiguration>();
        _ = services.AddHttpClient<OpenAiClient>(client =>
            client.Timeout = TimeSpan.FromMinutes(2));
        _ = services.AddSingleton<MockOpenAiClient>();
        _ = services.AddTransient<IOpenAiClient>(provider =>
            provider.GetRequiredService<IConfiguration>()
                .GetValue<bool>("FormAi:UseMock")
                ? provider.GetRequiredService<MockOpenAiClient>()
                : provider.GetRequiredService<OpenAiClient>());
        return services;
    }
}
