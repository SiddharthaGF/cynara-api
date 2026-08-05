using System.Text.Json;
using System.Text.Json.Serialization;

using Cynara.Api.JsonApi;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Api.JsonApi.Services;
using Cynara.Application;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.OpenApi.Swashbuckle;
using JsonApiDotNetCore.Resources.Annotations;

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.Hosting;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCynaraApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services = services
            .AddCors(options =>
            {
                options.AddDefaultPolicy(
                    policy =>
                    {
                        // Dev/maquette CORS: origins are not yet locked down.
                        _ = policy
                            .AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            })
            .AddCynaraForwardedHeaders(configuration)
            .AddCynaraApplication()
            .AddCynaraInfrastructure(configuration)
            .AddCynaraHospitalContext(configuration)
            .AddSingleton(TimeProvider.System)
            .AddHttpContextAccessor();

        _ = services.AddControllers();

        services = services
            .AddJsonApi<CynaraDbContext>(
                options =>
                {
                    options.Namespace = "api";
                    options.DefaultPageSize = new PageSize(20);
                    options.MaximumPageSize = new PageSize(100);
                    options.MaximumIncludeDepth = 3;
                    options.IncludeTotalResourceCount = true;
                    options.AllowUnknownQueryStringParameters = true;
                    options.DefaultAttrCapabilities = AttrCapabilities.All;
                    options.SerializerOptions.PropertyNamingPolicy =
                        JsonNamingPolicy.CamelCase;
                    options.SerializerOptions.Converters.Add(
                        new JsonStringEnumConverter(
                            JsonNamingPolicy.CamelCase));
                })
            .AddCynaraJsonApiResourceServices()
            .AddScoped<
                JsonApiDotNetCore.Middleware.IExceptionHandler,
                CynaraJsonApiExceptionHandler>();

        // Returns void; must not be chained.
        services.AddOpenApiForJsonApi(swagger =>
        {
            swagger.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = "Cynara API",
                    Version = "v1",
                    Description =
                        "JSON:API contract for Cynara clinical form "
                        + "lifecycle, responses, components, patients, "
                        + "encounters, clinical taxonomy, audit, and "
                        + "AI provider settings. Send `X-Actor-Id` on "
                        + "mutating requests for audit attribution and "
                        + "`X-Hospital-Code` on every request to select "
                        + "the hospital workspace. The tenant context is "
                        + "resolved by the API host and cannot be "
                        + "overridden by client-supplied identifiers. "
                        + "Media type: application/vnd.api+json. Workflow "
                        + "actions use rowVersion query parameters. "
                        + "Form AI status/chat use application/json; "
                        + "chat/stream uses text/event-stream (SSE).",
                });
            swagger.AddSecurityDefinition(
                "ActorId",
                new OpenApiSecurityScheme
                {
                    Description =
                        "Optional actor identity for audit attribution. "
                        + "Not an authentication gate in this maquette.",
                    Name = "X-Actor-Id",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                });
            swagger.AddSecurityDefinition(
                "HospitalCode",
                new OpenApiSecurityScheme
                {
                    Description =
                        "Required hospital workspace code. Selects the "
                        + "tenant scope for the request. Unknown, missing, "
                        + "or inactive codes are rejected before any "
                        + "workflow runs.",
                    Name = "X-Hospital-Code",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                });

            // ActorIdOperationFilter is registered in
            // CynaraSwaggerGenConfigureOptions (after JADNC docs filter).
            swagger.DocumentFilter<CynaraOpenApiDocumentFilter>();

            IncludeXmlDocumentation(swagger);
        });

        // After JADNC ConfigureSwaggerGenOptions: title-case tags and add
        // X-Actor-Id only after get-by-id documentation is applied.
        _ = services.AddSingleton<
            IConfigureOptions<SwaggerGenOptions>,
            CynaraSwaggerGenConfigureOptions>();

        return services;
    }

    private static IServiceCollection AddCynaraForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!string.Equals(
                configuration["RENDER_SERVICE_TYPE"],
                "web",
                StringComparison.OrdinalIgnoreCase))
        {
            return services;
        }

        _ = services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.ForwardLimit = 1;
        });

        return services;
    }

    private static void IncludeXmlDocumentation(SwaggerGenOptions swagger)
    {
        ArgumentNullException.ThrowIfNull(swagger);
        IncludeXmlIfPresent(swagger, "Cynara.Api.xml", includeControllerComments: true);
        IncludeXmlIfPresent(swagger, "Cynara.Domain.xml", includeControllerComments: false);
        IncludeXmlIfPresent(swagger, "Cynara.Application.xml", includeControllerComments: false);
    }

    private static void IncludeXmlIfPresent(
        SwaggerGenOptions swagger,
        string fileName,
        bool includeControllerComments)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(path))
        {
            swagger.IncludeXmlComments(path, includeControllerComments);
        }
    }
}
