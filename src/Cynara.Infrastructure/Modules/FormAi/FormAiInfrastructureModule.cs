using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.DependencyInjection;

using Polly;
using Polly.Retry;

namespace Cynara.Infrastructure.Modules.FormAi;

public static class FormAiInfrastructureModule
{
    public static IServiceCollection AddFormAiInfrastructureModule(
        this IServiceCollection services)
    {
        _ = services.AddSingleton<IOpenAiConfiguration, OpenAiConfiguration>();
        _ = services.AddSingleton<IFormAiSkillLoader, FileFormAiSkillLoader>();
        _ = services.AddSingleton<IOpenAiChatClientFactory, OpenAiChatClientFactory>();
        _ = services.AddSingleton<IOpenAiClient, OpenAiClient>();
        _ = services.AddResiliencePipeline(
            OpenAiClient.ResiliencePipelineKey,
            builder =>
            {
                _ = builder.AddRetry(new RetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(250),
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = new PredicateBuilder()
                        .Handle<HttpRequestException>()
                        .Handle<System.ClientModel.ClientResultException>(
                            static ex => ProviderStatusRules.IsTransient(ex.Status))
                        .Handle<IOException>(),
                });
                _ = builder.AddTimeout(TimeSpan.FromMinutes(2));
            });
        return services;
    }
}
