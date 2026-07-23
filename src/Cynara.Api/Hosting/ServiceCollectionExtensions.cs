using Cynara.Application;
using Cynara.Infrastructure;

namespace Cynara.Api.Hosting;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOpenApi();

        string[] allowedCorsOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? [];
        _ = services.AddCors(options =>
        {
            options.AddDefaultPolicy(
                policy => policy
                    .WithOrigins(allowedCorsOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod());
        });

        _ = services.AddCynaraApplication();
        _ = services.AddCynaraInfrastructure(configuration);
        _ = services.AddSingleton(TimeProvider.System);

        return services;
    }
}
