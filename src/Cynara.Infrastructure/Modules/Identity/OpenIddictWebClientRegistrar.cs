using OpenIddict.Abstractions;

namespace Cynara.Infrastructure.Modules.Identity;

internal static class OpenIddictWebClientRegistrar
{
    /// <summary>
    /// Idempotently ensures an OIDC client exists with the given redirect
    /// URIs. An existing client is updated add-only (URIs are merged, never
    /// removed) so out-of-band registrations keep their permissions and any
    /// manually added URIs. Creating a new client requires a secret.
    /// </summary>
    public static async Task EnsureAsync(
        IOpenIddictApplicationManager applications,
        string clientId,
        string? clientSecret,
        IReadOnlyList<string> redirectUris,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(applications);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        object? existing = await applications
            .FindByClientIdAsync(clientId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await applications
                .PopulateAsync(descriptor, existing, cancellationToken)
                .ConfigureAwait(false);
            foreach (string redirectUri in redirectUris)
            {
                _ = descriptor.RedirectUris.Add(
                    new Uri(redirectUri, UriKind.Absolute));
            }

            await applications
                .UpdateAsync(existing, descriptor, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException(
                $"OIDC client '{clientId}' does not exist and no secret is "
                + "configured; set the secret before provisioning it.");
        }

        var webClient = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "Cynara Web",
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
        };
        AddStandardWebClientPermissions(webClient);
        foreach (string redirectUri in redirectUris)
        {
            _ = webClient.RedirectUris.Add(
                new Uri(redirectUri, UriKind.Absolute));
        }

        _ = await applications
            .CreateAsync(webClient, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void AddStandardWebClientPermissions(
        OpenIddictApplicationDescriptor descriptor)
    {
        descriptor.Permissions.UnionWith(
        [
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.Endpoints.Revocation,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Scopes.Email,
            OpenIddictConstants.Permissions.Scopes.Profile,
            "scp:openid",
            "scp:offline_access",
            "scp:cynara_api",
        ]);
    }
}
