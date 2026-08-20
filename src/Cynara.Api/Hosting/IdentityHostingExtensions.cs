using Cynara.Api.Common.ActorContext;
using Cynara.Infrastructure.Modules.Identity;

using Microsoft.AspNetCore.Identity;

using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace Cynara.Api.Hosting;

/// <summary>
/// Registers ASP.NET Core Identity, the OpenIddict server and validation
/// pipeline, and the principal-backed actor services used by the API host.
/// The server exposes authorization-code + PKCE, refresh, client-credentials,
/// and revocation flows over <c>/connect/*</c>; validation runs in-process
/// against the local server so the development host and integration tests can
/// mint and consume real tokens without an external IdP.
/// </summary>
internal static class IdentityHostingExtensions
{
    /// <summary>Audience claim required of access tokens targeting the API.</summary>
    public const string Audience = "cynara-api";

    /// <summary>Application-level scope beyond the standard OIDC scopes.</summary>
    public const string ApiScope = "cynara_api";

    public static IServiceCollection AddCynaraIdentity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services
            .AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<CynaraIdentityDbContext>()
            .AddDefaultTokenProviders();

        // Development-only email sink for Identity recovery messages. The
        // Development guard lives inside the sender (it logs locally and
        // performs no external transport in any environment), so registration
        // stays environment-agnostic.
        _ = services.AddSingleton<
            IEmailSender<IdentityUser<Guid>>,
            DevelopmentEmailSender>();

        string issuer = configuration["OpenIddict:Issuer"] ?? "http://localhost:5000";
        bool isDevelopment = IsDevelopmentEnvironment(configuration);

        _ = services
            .AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<CynaraIdentityDbContext>())
            .AddServer(options => ConfigureServer(options, issuer, isDevelopment))
            .AddValidation(options =>
            {
                _ = options.AddAudiences(Audience);
                _ = options.UseLocalServer();
                _ = options.UseAspNetCore();
            });

        _ = services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultScheme =
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme =
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            });

        _ = services.AddScoped<ResolvedActor>();

        return services;
    }

    private static void ConfigureServer(
        OpenIddictServerBuilder options,
        string issuer,
        bool isDevelopment)
    {
        _ = options
            .SetAuthorizationEndpointUris("/connect/authorize")
            .SetTokenEndpointUris("/connect/token")
            .SetRevocationEndpointUris("/connect/revocation");

        _ = options
            .AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow()
            .AllowClientCredentialsFlow();

        // The resource-owner password grant is only ever enabled in
        // Development; the TokenController enforces the same guard so the
        // server-side registration and the endpoint both stay closed in
        // production (ROPC is unsafe for cynara-web).
        if (isDevelopment)
        {
            _ = options.AllowPasswordFlow();
        }

        _ = options.RegisterScopes(
            OpenIddictConstants.Scopes.OpenId,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            ApiScope);

        _ = options.RegisterAudiences(Audience);

        _ = options
            .SetAccessTokenLifetime(TimeSpan.FromMinutes(15))
            .SetRefreshTokenLifetime(TimeSpan.FromDays(7))
            .SetIdentityTokenLifetime(TimeSpan.FromMinutes(15));

        _ = options.AddDevelopmentSigningCertificate();
        _ = options.AddDevelopmentEncryptionCertificate();

        // Access tokens are stored as reference tokens so revocation actually
        // invalidates them on local validation. Disable the transport
        // security requirement so HTTP dev/test hosts can exercise the flows.
        _ = options.UseReferenceAccessTokens();

        _ = options.SetIssuer(issuer);

        // Cache interactive authorization requests as server-owned request
        // tokens so the login form only round-trips the opaque request_uri
        // transaction handle. Expiry and replay validation stay inside
        // OpenIddict; bound the lifetime so stale login pages expire fast.
        _ = options.EnableAuthorizationRequestCaching();
        _ = options.Configure(serverOptions =>
            serverOptions.RequestTokenLifetime = TimeSpan.FromMinutes(5));

        _ = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough()
            .EnableTokenEndpointPassthrough()
            .DisableTransportSecurityRequirement();
    }

    private static bool IsDevelopmentEnvironment(IConfiguration configuration)
    {
        string? name = configuration["environment"]
            ?? configuration["ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"];
        return string.Equals(name, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
