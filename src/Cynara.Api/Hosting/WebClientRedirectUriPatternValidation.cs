using System.Collections.Immutable;

using OpenIddict.Abstractions;
using OpenIddict.Server;

#pragma warning disable S1075 // URIs should not be hardcoded: mirrors the
// OpenIddict documentation anchor returned by the native validator.
namespace Cynara.Api.Hosting;

/// <summary>
/// Replaces the built-in <c>ValidateClientRedirectUri</c> server event
/// handler. The exact-match comparison against the redirect URIs registered
/// on the OIDC client is preserved verbatim (Ordinal and OrdinalIgnoreCase),
/// so existing production and development registrations behave identically.
/// On top of that baseline, redirect URIs from the configured web client may
/// satisfy the regex patterns bound by
/// <see cref="WebClientRedirectUriPatternPolicy"/> — a single anchored
/// expression can cover both the production origin and ephemeral preview
/// origins. Every other OAuth/OIDC validation remains handled by the
/// untouched native pipeline.
/// </summary>
public sealed partial class WebClientRedirectUriPatternValidation(
    IOpenIddictApplicationManager applicationManager,
    WebClientRedirectUriPatternPolicy patternPolicy,
    ILogger<WebClientRedirectUriPatternValidation> logger)
    : IOpenIddictServerHandler<OpenIddictServerEvents
        .ValidateAuthorizationRequestContext>
{
    private const string InvalidRedirectUriDocumentation =
        "https://documentation.openiddict.com/errors/ID2043";

    private const string InvalidRedirectUriDescription =
        "The specified 'redirect_uri' is not valid for this client "
        + "application.";

    /// <summary>Descriptor occupying the removed native handler's slot.</summary>
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor
            .CreateBuilder<OpenIddictServerEvents
                .ValidateAuthorizationRequestContext>()
            .UseScopedHandler<WebClientRedirectUriPatternValidation>()
            .SetOrder(OpenIddictServerHandlers.Authentication
                .ValidateClientRedirectUri.Descriptor.Order)
            .Build();

    /// <inheritdoc/>
    public async ValueTask HandleAsync(
        OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? address = context.Request.RedirectUri;
        if (string.IsNullOrEmpty(address))
        {
            // Presence and format are enforced by the parameter-level native
            // handlers; nothing to extend here.
            return;
        }

        string clientId = context.ClientId ?? string.Empty;

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || !uri.IsWellFormedOriginalString())
        {
            RejectInvalidRedirectUri(context);
            return;
        }

        object? application = await applicationManager
            .FindByClientIdAsync(clientId, context.Transaction.CancellationToken)
            .ConfigureAwait(false);
        if (application is null)
        {
            // Unknown clients are rejected by the native client validation;
            // do not mask that outcome with a redirect-specific error.
            return;
        }

        ImmutableArray<string> registered = await applicationManager
            .GetRedirectUrisAsync(application, context.Transaction.CancellationToken)
            .ConfigureAwait(false);
        if (registered.Any(candidate =>
                MatchesRegistered(candidate, address)))
        {
            LogRegisteredRedirectAccepted(address, clientId);
            return;
        }

        if (patternPolicy.Matches(clientId, uri))
        {
            LogPatternRedirectAccepted(address, clientId);
            return;
        }

        RejectInvalidRedirectUri(context);
    }

    private static bool MatchesRegistered(
        string candidate,
        string address)
    {
        return string.Equals(candidate, address, StringComparison.Ordinal)
            || string.Equals(
                candidate,
                address,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectInvalidRedirectUri(
        OpenIddictServerEvents.ValidateAuthorizationRequestContext context)
    {
        context.Reject(
            error: OpenIddictConstants.Errors.InvalidRequest,
            description: InvalidRedirectUriDescription,
            uri: InvalidRedirectUriDocumentation);
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Accepted registered redirect URI {RedirectUri} for "
            + "client {ClientId}.")]
    private partial void LogRegisteredRedirectAccepted(
        string redirectUri,
        string clientId);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Information,
        Message = "Accepted redirect URI {RedirectUri} for client "
            + "{ClientId} via configured pattern.")]
    private partial void LogPatternRedirectAccepted(
        string redirectUri,
        string clientId);
}
