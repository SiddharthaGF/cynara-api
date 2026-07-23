using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.Modules.Audit;
using Cynara.Api.Modules.Components;
using Cynara.Api.Modules.FormAi;
using Cynara.Api.Modules.FormResponses;
using Cynara.Api.Modules.Forms;
using Cynara.Api.Modules.Health;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Preview;

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
        _ = app.UseCors();

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
            _ = app.MapOpenApi();
        }

        app.MapCynaraEndpoints();

        return app;
    }

    private static void MapCynaraEndpoints(this WebApplication app)
    {
        _ = app.MapGet("/", () => Results.Text("Cynara API"));
        _ = app.MapComponentEndpoints();
        _ = app.MapFormEndpoints();
        _ = app.MapFormResponseEndpoints();
        _ = app.MapFormAiEndpoints();
        _ = app.MapAuditEndpoints();
        _ = app.MapHealthEndpoints();
    }
}
