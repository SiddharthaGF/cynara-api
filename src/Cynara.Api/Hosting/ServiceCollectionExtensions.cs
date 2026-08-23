using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

using Cynara.Api.CapabilityAuthorization;
using Cynara.Api.Common.ActorContext;
using Cynara.Api.JsonApi;
using Cynara.Api.JsonApi.OpenApi;
using Cynara.Api.JsonApi.Services;
using Cynara.Application;
using Cynara.Application.Modules.Capabilities;
using Cynara.Infrastructure;
using Cynara.Infrastructure.Modules.Identity;
using Cynara.Infrastructure.Persistence;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.OpenApi.Swashbuckle;
using JsonApiDotNetCore.Resources.Annotations;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.Hosting;

internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers cross-cutting API services. Missing or empty
    /// <c>Cors:AllowedOrigins</c> rejects every cross-origin request.
    /// <c>Cors:AllowedOriginPatterns</c> adds regex-based origin matching
    /// for ephemeral preview deployments (e.g. per-PR Workers subdomains).
    /// </summary>
    public static IServiceCollection AddCynaraApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services = services
            .AddCors(options =>
            {
                options.AddDefaultPolicy(
                    policy =>
                    {
                        string[]? allowedOrigins = configuration
                            .GetSection("Cors:AllowedOrigins")
                            .Get<string[]>();
                        string[]? allowedPatterns = configuration
                            .GetSection("Cors:AllowedOriginPatterns")
                            .Get<string[]>();
                        Regex[] patterns = [..
                            (allowedPatterns ?? [])
                                .Select(pattern =>
                                    new Regex(
                                        pattern,
                                        RegexOptions.Compiled,
                                        TimeSpan.FromSeconds(1)))];
                        _ = policy
                            .SetIsOriginAllowed(origin => IsAllowedOrigin(
                                origin,
                                allowedOrigins,
                                patterns))
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            })
            .AddCynaraForwardedHeaders(configuration)
            .AddCynaraApplication()
            .AddCynaraDataProtection()
            .AddCynaraInfrastructure(configuration)
            .AddCynaraHospitalContext(configuration)
            .AddCynaraIdentity(configuration, environment)
            .AddSingleton(TimeProvider.System)
            .AddHttpContextAccessor();

        _ = services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<
                HttpContext,
                string>(context =>
            {
                if (!IsCredentialBearingEndpoint(context))
                {
                    return RateLimitPartition.GetNoLimiter("excluded");
                }

                string partition = context.Connection.RemoteIpAddress?
                    .ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partition,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
            options.OnRejected = static async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out TimeSpan retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                            .ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsync(
                    "Too many authentication attempts. Try again later.",
                    cancellationToken).ConfigureAwait(false);
            };
        });

        services = AddHostCurrentActor(services);

        _ = services.AddControllers(
            options => options.Filters.Add<CapabilityAuthorizationFilter>());

        services = AddCynaraCapabilityAuthorization(services);

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

        services = AddCynaraOpenApi(services);

        return services;
    }

    /// <summary>
    /// Persists the DataProtection key ring in the identity database so
    /// refresh tokens and authorization artifacts survive restarts and
    /// scaled instances.
    /// </summary>
    internal static IServiceCollection AddCynaraDataProtection(
        this IServiceCollection services)
    {
        _ = services.AddDataProtection()
            .PersistKeysToDbContext<CynaraIdentityDbContext>()
            .SetApplicationName("cynara-api");
        return services;
    }

    /// <summary>
    /// Configures the JADNC Swagger document. Bearer and OAuth2 security
    /// schemes are registered by <see
    /// cref="CynaraSwaggerGenConfigureOptions"/>, which is also added after
    /// JADNC's ConfigureSwaggerGenOptions so get-by-id documentation applies
    /// first.
    /// </summary>
    private static IServiceCollection AddCynaraOpenApi(
        IServiceCollection services)
    {
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
                        + "assignment, AI provider settings, workflow "
                        + "pipelines and journeys, and clinical tasks. Send "
                        + "`X-Hospital-Code` on every request to select the "
                        + "hospital workspace; the tenant context is resolved "
                        + "by the API host and cannot be overridden by "
                        + "client-supplied identifiers. Protected endpoints "
                        + "require a bearer access token issued by the OIDC "
                        + "`/connect` surface (authorization-code + PKCE or "
                        + "client credentials) and a capability the resolved "
                        + "actor holds in the selected hospital; missing or "
                        + "invalid tokens return 401, and ungranted actors "
                        + "receive 403 without revealing whether the protected "
                        + "resource exists. Media type: application/vnd.api+json. "
                        + "Workflow actions use rowVersion query parameters; "
                        + "pipeline and task transitions carry the concurrency "
                        + "token in the request body. Form AI status/chat use "
                        + "application/json; chat/stream uses "
                        + "text/event-stream (SSE).",
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

            swagger.DocumentFilter<CynaraOpenApiDocumentFilter>();

            IncludeXmlDocumentation(swagger);
        });

        _ = services.AddSingleton<
            IConfigureOptions<SwaggerGenOptions>,
            CynaraSwaggerGenConfigureOptions>();

        return services;
    }

    /// <summary>
    /// Replaces the Application-layer DefaultCurrentActor registration: the
    /// host must win regardless of add-after ordering. Production resolves
    /// the actor from token sub + hospital and never reads X-Actor-Id.
    /// </summary>
    private static IServiceCollection AddHostCurrentActor(
        IServiceCollection services)
    {
        return services.Replace(
            ServiceDescriptor.Scoped<ICurrentActor, PrincipalCurrentActor>());
    }

    private static bool IsCredentialBearingEndpoint(HttpContext context)
    {
        return HttpMethods.IsPost(context.Request.Method)
            && (context.Request.Path.Equals(
                    "/connect/authorize",
                    StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.Equals(
                    "/connect/account/recovery",
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Registers capability authorization with a fallback policy requiring an
    /// authenticated user, so endpoints lacking explicit authorization
    /// metadata still challenge anonymous callers with 401; public paths opt
    /// out explicitly.
    /// </summary>
    private static IServiceCollection AddCynaraCapabilityAuthorization(
        IServiceCollection services)
    {
        return services
            .AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            })
            .AddSingleton<
                IAuthorizationPolicyProvider,
                CapabilityPolicyProvider>()
            .AddScoped<IAuthorizationHandler, CapabilityAuthorizationHandler>()
            .AddSingleton<
                IAuthorizationMiddlewareResultHandler,
                CapabilityAuthorizationMiddlewareResultHandler>();
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

    /// <summary>
    /// Allows an origin when it is listed explicitly or matches one of the
    /// configured regex patterns; empty lists deny every cross-origin call.
    /// </summary>
    private static bool IsAllowedOrigin(
        string origin,
        string[]? allowedOrigins,
        Regex[] allowedPatterns)
    {
        return (allowedOrigins ?? []).Contains(origin)
            || allowedPatterns.Any(pattern => pattern.IsMatch(origin));
    }
}
