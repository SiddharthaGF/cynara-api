using System.Text;

using Cynara.Api.Common.ErrorHandling;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Api.Modules.Health;
using Cynara.Api.Modules.Pipelines;
using Cynara.Api.Modules.Schemas;
using Cynara.Api.Modules.Tasks;
using Cynara.Api.Modules.Users;
using Cynara.Application.Modules.Hospitals;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Hospitals;
using Cynara.Infrastructure.Modules.Preview;

using JsonApiDotNetCore.Configuration;

using Microsoft.OpenApi;

using Scalar.AspNetCore;

using Swashbuckle.AspNetCore.Swagger;

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
        _ = app.UseRateLimiter();
        _ = app.UseCors();
        _ = app.UseAuthentication();
        _ = app.UseHospitalContext();
        _ = app.UseMembershipResolution();
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

        // Development and preview instances seed real users, hospital
        // memberships, capability assignments, and the confidential
        // cynara-web client so the demo login works end to end. The seed is
        // idempotent and never runs in production.
        if (app.Environment.IsDevelopment() || IsPreviewEnvironment(app.Configuration))
        {
            await app.Services.SeedAuthDevDataAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (IsPreviewEnvironment(app.Configuration))
        {
            await app.Services.SeedPreviewDemoAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (app.Environment.IsDevelopment())
        {
            // Serve /swagger/{document}/swagger.json ourselves (before the
            // default Swashbuckle middleware) so the live Development document
            // matches the committed contracts/openapi.json exactly: same compact
            // serialization and the same security-requirement transform.
            _ = app.UseCynaraSwaggerJson();
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

    /// <summary>
    /// Intercepts <c>GET /swagger/{documentName}/swagger.json</c> and responds
    /// with the compact, security-correct document produced by
    /// <see cref="OpenApiDocumentSerializer"/>, mirroring the committed
    /// <c>contracts/openapi.json</c>. Non-JSON Swagger requests fall through to
    /// the regular Swashbuckle middleware. This is required because the
    /// Swashbuckle middleware serializes with its own writer and would otherwise
    /// emit degenerate empty security requirements.
    /// </summary>
    public static IApplicationBuilder UseCynaraSwaggerJson(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        const string SwaggerJsonSuffix = "/swagger.json";
        var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        return app.Use(async (context, next) =>
        {
            HttpRequest request = context.Request;
            if (HttpMethods.IsGet(request.Method)
                && request.Path.HasValue
                && request.Path.Value.StartsWith(
                    "/swagger/",
                    StringComparison.OrdinalIgnoreCase)
                && request.Path.Value.EndsWith(
                    SwaggerJsonSuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                string documentName = request.Path.Value[
                    "/swagger/".Length..^SwaggerJsonSuffix.Length];
                if (!string.IsNullOrWhiteSpace(documentName))
                {
                    ISwaggerProvider provider = context.RequestServices
                        .GetRequiredService<ISwaggerProvider>();
                    string? basePath = request.PathBase.HasValue
                        ? request.PathBase.Value
                        : null;

                    OpenApiDocument document = provider.GetSwagger(
                        documentName,
                        host: null,
                        basePath);

                    string json = OpenApiDocumentSerializer.Serialize(document);
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json;charset=utf-8";
                    await context.Response
                        .WriteAsync(json, utf8WithoutBom, context.RequestAborted)
                        .ConfigureAwait(false);
                    return;
                }
            }

            await next(context).ConfigureAwait(false);
        });
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
        _ = app.MapUsersEndpoints();
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
