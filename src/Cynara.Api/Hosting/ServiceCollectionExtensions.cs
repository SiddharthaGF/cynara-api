using System.Text.Json;
using System.Text.Json.Serialization;

using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Api.JsonApi;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Api.JsonApi.Services;
using Cynara.Application;
using Cynara.Application.Modules.Capabilities;
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
#pragma warning disable S5122 // Allow-any is deliberate for the maquette
                        // surface: it carries no credentials and no
                        // authentication gate.
                        _ = policy
                            .AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod();
#pragma warning restore S5122
                    });
            })
            .AddCynaraForwardedHeaders(configuration)
            .AddCynaraApplication()
            .AddCynaraInfrastructure(configuration)
            .AddCynaraHospitalContext(configuration)
            .AddSingleton(TimeProvider.System)
            .AddHttpContextAccessor()
            .AddScoped<ICurrentActor, CurrentActor>();

        _ = services.AddControllers(options =>
        {
            _ = options.Filters.Add<CapabilityAuthorizationFilter>();
        });

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
                        + "encounters, clinical taxonomy, audit, capability "
                        + "assignment, and AI provider settings. Send "
                        + "`X-Actor-Id` on every request: it is the actor "
                        + "identity for both audit attribution and capability "
                        + "resolution. Send `X-Hospital-Code` on every "
                        + "request to select the hospital workspace; the "
                        + "tenant context is resolved by the API host and "
                        + "cannot be overridden by client-supplied "
                        + "identifiers. Stage 2 protected endpoints require "
                        + "a capability the actor holds in the resolved "
                        + "hospital; denied requests return 403 and never "
                        + "reveal whether the protected resource exists. "
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
                        "Actor identity used for both audit attribution and "
                        + "capability resolution. Protected endpoints require "
                        + "the actor to hold the needed capability in the "
                        + "resolved hospital; missing or ungranted actors "
                        + "receive 403.",
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
