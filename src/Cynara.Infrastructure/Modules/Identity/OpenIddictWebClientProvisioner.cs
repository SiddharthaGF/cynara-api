using Microsoft.Extensions.Configuration;

using OpenIddict.Abstractions;

namespace Cynara.Infrastructure.Modules.Identity;

/// <summary>
/// Config-driven OIDC client provisioning for every environment. Reads
/// <c>OpenIddict:WebClient:RedirectOrigins</c> and merges the derived
/// <c>/en/login</c> + <c>/es/login</c> URIs into the web client at startup;
/// a no-op when nothing is configured.
/// </summary>
public static class OpenIddictWebClientProvisioner
{
    public const string SectionName = "OpenIddict:WebClient";

    /// <summary>Applies configured redirect origins to the web client.</summary>
    public static async Task ProvisionOpenIddictWebClientAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        AsyncServiceScope scope = services.CreateAsyncScope();
        try
        {
            IServiceProvider provider = scope.ServiceProvider;
            IConfiguration configuration = provider
                .GetRequiredService<IConfiguration>();
            string[]? origins = configuration
                .GetSection($"{SectionName}:RedirectOrigins")
                .Get<string[]>();
            if (origins is not { Length: > 0 })
            {
                return;
            }

            IOpenIddictApplicationManager applications = provider
                .GetRequiredService<IOpenIddictApplicationManager>();
            await OpenIddictWebClientRegistrar.EnsureAsync(
        applications,
        configuration[$"{SectionName}:ClientId"] ?? "cynara-web",
        configuration[$"{SectionName}:Secret"],
        WebLoginRedirectUriBuilder.Build(origins),
        cancellationToken)
    .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
