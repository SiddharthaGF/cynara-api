using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.Modules.Health;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Preview;

using JsonApiDotNetCore.Configuration;

using Scalar.AspNetCore;

namespace Cynara.Api.Hosting;

internal static class WebApplicationExtensions
{
    public static async Task<WebApplication> UseCynaraApiAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.Use(
            async (context, next) =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next().ConfigureAwait(false);
            });
        _ = app.UseCynaraExceptionHandling();
        _ = app.UseRouting();
        _ = app.UseCors();
        app.UseJsonApi();

        await app.Services.InitializeDatabaseAsync(cancellationToken)
            .ConfigureAwait(false);

        if (InfrastructureServiceCollectionExtensions.IsPreviewStorage(
                app.Configuration))
        {
            await app.Services.SeedPreviewDemoAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (app.Environment.IsDevelopment())
        {
            _ = app.UseSwagger();
            _ = app.MapScalarApiReference(options =>
            {
                _ = options.WithTitle("Cynara API");
                _ = options.WithOpenApiRoutePattern(
                    "/swagger/{documentName}/swagger.json");
            });
        }

        // Probe/root endpoints stay out of Scalar; JSON:API is the contract UI.
        _ = app.MapHealthEndpoints();
        _ = app.MapControllers();

        return app;
    }
}
