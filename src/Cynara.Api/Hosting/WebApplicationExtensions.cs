using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.Modules.Health;
using Cynara.Api.Modules.Pipelines;
using Cynara.Api.Modules.Schemas;
using Cynara.Api.Modules.Tasks;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Hospitals;
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

        _ = app.UseCynaraExceptionHandling();
        _ = app.UseForwardedHeaders();
        _ = app.Use(
            (context, next) =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return next();
            });
        _ = app.UseRouting();
        _ = app.UseCors();
        _ = app.UseHospitalContext();
        _ = app.UseAuthorization();
        app.UseJsonApi();

        await app.Services.InitializeDatabaseAsync(cancellationToken)
            .ConfigureAwait(false);

        HospitalBootstrapOptions hospitalOptions = app.Configuration
            .GetSection(HospitalBootstrapOptions.SectionName)
            .Get<HospitalBootstrapOptions>() ?? new();
        await app.Services
            .EnsureBootstrapHospitalAsync(hospitalOptions, cancellationToken)
            .ConfigureAwait(false);

        if (IsPreviewEnvironment(app.Configuration))
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
        _ = app.MapSchemaEndpoints();
        _ = app.MapMinimalApiEndpoints();
        _ = app.MapControllers();

        return app;
    }

    public static WebApplication MapMinimalApiEndpoints(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Single source of truth for the minimal-API modules that appear in
        // the OpenAPI contract. The exporter calls this too so a new module
        // mapped here is automatically included in contracts/openapi.json.
        _ = app.MapPipelinesEndpoints();
        _ = app.MapTasksEndpoints();
        return app;
    }

    private static bool IsPreviewEnvironment(IConfiguration configuration)
    {
        // Render sets `IS_PULL_REQUEST=true` on every PR preview instance and
        // `IS_PULL_REQUEST=false` on the main service; the variable is not
        // present in local dev.
        string? value = configuration["IS_PULL_REQUEST"];
        return bool.TryParse(value, out bool isPreview) && isPreview;
    }
}
