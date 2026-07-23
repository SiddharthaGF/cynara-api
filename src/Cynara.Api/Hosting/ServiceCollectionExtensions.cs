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

        string[] allowedCorsOrigins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?? [];

        services = services
            .AddCors(options =>
            {
                options.AddDefaultPolicy(
                    policy => policy
                        .WithOrigins(allowedCorsOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod());
            })
            .AddCynaraApplication()
            .AddCynaraInfrastructure(configuration)
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
                        + "lifecycle, responses, components, audit, and "
                        + "Form AI workflows. Send `X-Actor-Id` on "
                        + "mutating requests for audit attribution. Media "
                        + "type: application/vnd.api+json. Workflow "
                        + "actions use rowVersion query parameters. "
                        + "Form AI chat/settings use application/json.",
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
            swagger.OperationFilter<ActorIdOperationFilter>();
            swagger.DocumentFilter<CynaraOpenApiDocumentFilter>();

            string apiXml = Path.Combine(
                AppContext.BaseDirectory,
                "Cynara.Api.xml");
            string domainXml = Path.Combine(
                AppContext.BaseDirectory,
                "Cynara.Domain.xml");
            if (File.Exists(apiXml))
            {
                swagger.IncludeXmlComments(
                    apiXml,
                    includeControllerXmlComments: true);
            }

            if (File.Exists(domainXml))
            {
                swagger.IncludeXmlComments(
                    domainXml,
                    includeControllerXmlComments: false);
            }
        });

        // After JADNC ConfigureSwaggerGenOptions so Title Case wraps its
        // TagsSelector (and XmlCommentsDocumentFilter stays aligned).
        _ = services.AddSingleton<
            IConfigureOptions<SwaggerGenOptions>,
            TitleCaseOpenApiTagsConfigureOptions>();

        return services;
    }
}
