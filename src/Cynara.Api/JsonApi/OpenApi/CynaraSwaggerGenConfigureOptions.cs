using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Post-JsonApiDotNetCore Swagger tweaks that must run after
/// <c>ConfigureSwaggerGenOptions</c>:
/// <list type="bullet">
/// <item>
/// Title-case OpenAPI tags so Scalar matches controller tags like "Form AI".
/// </item>
/// <item>
/// Register the bearer/OAuth2 security schemes and their operation filter last
/// so JADNC's documentation filter can still distinguish collection (0 params)
/// vs get-by-id (1 param) before the security requirement is applied.
/// </item>
/// </list>
/// </summary>
internal sealed class CynaraSwaggerGenConfigureOptions(
    IConfiguration configuration)
    : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Func<ApiDescription, IList<string>> inner =
            options.SwaggerGeneratorOptions.TagsSelector;

        options.TagActionsBy(description =>
        {
            IList<string> tags = inner(description);
            if (tags.Count == 0)
            {
                return tags;
            }

            return [.. tags.Select(OpenApiTagNames.ToTitleCase)];
        });

        // Bearer access tokens issued by the /connect surface. Clients use the
        // OAuth2 flow below; the bearer scheme is what protects each operation.
        options.AddSecurityDefinition(
            OpenApiSecurity.Bearer,
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "Cynara API access token issued via the OIDC /connect "
                    + "surface (authorization-code + PKCE or client "
                    + "credentials). Send it as: Authorization: Bearer "
                    + "&lt;token&gt;. Protected endpoints also require the "
                    + "X-Hospital-Code header.",
            });

        options.AddSecurityDefinition(
            OpenApiSecurity.OAuth2,
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Description =
                    "OIDC flows against the Cynara authorization server.",
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = BuildUrl("connect/authorize"),
                        TokenUrl = BuildUrl("connect/token"),
                        Scopes = BuildScopes(
                            "openid",
                            "profile",
                            "email",
                            "offline_access",
                            "cynara_api"),
                    },
                    ClientCredentials = new OpenApiOAuthFlow
                    {
                        TokenUrl = BuildUrl("connect/token"),
                        Scopes = BuildScopes("cynara_api"),
                    },
                },
            });

        options.OperationFilter<BearerSecurityOperationFilter>();
        options.OperationFilter<HospitalCodeOperationFilter>();
        options.OperationFilter<WorkspaceOperationFilter>();
        options.OperationFilter<JsonApiErrorResponseFilter>();
        options.OperationFilter<FormAiStreamOperationFilter>();
        options.SchemaFilter<WorkspaceSchemaFilter>();
        options.SchemaFilter<CynaraEnumSchemaFilter>();
        options.SchemaFilter<ReadOnlyIdSchemaFilter>();
    }

    private Uri BuildUrl(string relativePath)
    {
        string issuer = ResolveIssuer();
        string baseUrl = issuer.TrimEnd('/');
        return new Uri(baseUrl + "/" + relativePath.TrimStart('/'), UriKind.Absolute);
    }

    private string ResolveIssuer()
    {
        // Mirrors IdentityHostingExtensions: absent config falls back to the
        // local Development listen URL so the exporter and local host agree.
        return configuration["OpenIddict:Issuer"] ?? "http://localhost:5000";
    }

    private static Dictionary<string, string> BuildScopes(params string[] names)
    {
        return names.ToDictionary(
            scope => scope,
            scope => $"Grants access to the {scope} scope.",
            StringComparer.Ordinal);
    }
}
