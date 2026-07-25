using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.Modules.Health;
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

        _ = app.UseForwardedHeaders();
        _ = app.Use(
            async (context, next) =>
            {
                context.Response.Headers.XContentTypeOptions = "nosniff";
                await next().ConfigureAwait(false);
            });
        _ = app.UseCynaraExceptionHandling();
        _ = app.UseRouting();
        _ = app.UseCors();
        _ = app.UseHospitalContext();
        app.UseJsonApi();

        await app.Services.InitializeDatabaseAsync(cancellationToken)
            .ConfigureAwait(false);

        HospitalBootstrapOptions hospitalOptions = ResolveHospitalOptions(app);
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
        _ = app.MapControllers();

        return app;
    }

    private static HospitalBootstrapOptions ResolveHospitalOptions(
        WebApplication app)
    {
        HospitalBootstrapOptions options = new();
        app.Configuration
            .GetSection(HospitalBootstrapOptions.SectionName)
            .Bind(options);
        return options;
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
