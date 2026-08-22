using System.Security.Claims;

using Cynara.Api.Hosting;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Cynara.Api.Modules.Identity;

/// <summary>
/// Interactive authorization endpoint shared by every environment. The
/// frontend owns the login visual surface; this endpoint remains the
/// credential and OpenIddict authority. Only the opaque cached request handle
/// crosses the frontend boundary.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connect")]
public sealed class AuthorizeController(
    UserManager<IdentityUser<Guid>> userManager,
    SignInManager<IdentityUser<Guid>> signInManager) : ControllerBase
{
    /// <summary>
    /// Handles the interactive authorization endpoint. GET hands the browser
    /// to the registered frontend login route; POST validates credentials and
    /// completes the flow through the cached authorization request.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the request is not a valid OpenIddict request.</exception>
    [HttpGet("authorize")]
    [HttpPost("authorize")]
    [IgnoreAntiforgeryToken]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S4502:Ensure CSRF protection is not disabled",
        Justification = "OpenIddict authorization endpoint: cross-site form posts are the OIDC contract. Anti-replay comes from the single-use server-cached request_uri handle, redirect-URI validation, and PKCE; antiforgery tokens cannot apply to third-party-initiated flows.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "S6932:Use model binding instead of accessing the raw request data",
        Justification = "The form fields are OpenIddict protocol payload parsed by the server middleware before MVC runs; MVC model binding would advertise a misleading API request body for this browser-driven protocol endpoint.")]
    public async Task<IActionResult> AuthorizeAsync()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The request is not a valid OpenIddict request.");

        // POST carries the credentials plus the opaque transaction handle.
        // OpenIddict restores the decoded request from its server-side cache.
        if (HttpMethods.IsPost(Request.Method))
        {
            string? email = Request.Form["email"].ToString();
            string? password = Request.Form["password"].ToString();

            IdentityUser<Guid>? user = await userManager.FindByNameAsync(
                userName: email ?? string.Empty).ConfigureAwait(false);
            if (user is not null && await signInManager
                .CheckPasswordSignInAsync(
                    user: user,
                    password: password ?? string.Empty,
                    lockoutOnFailure: true)
                .ConfigureAwait(false) is { Succeeded: true })
            {
                return CompleteAuthorization(user);
            }

            return RedirectToFrontendLogin(request, error: true);
        }

        // GET never renders backend HTML. The registered redirect URI has
        // already been validated by OpenIddict before this action runs.
        return RedirectToFrontendLogin(request, error: false);
    }

    private Microsoft.AspNetCore.Mvc.SignInResult CompleteAuthorization(
        IdentityUser<Guid> user)
    {
        ClaimsIdentity identity = new(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        _ = identity.SetClaim(
            OpenIddictConstants.Claims.Subject,
            user.Id.ToString());
        _ = identity.SetClaim(
            OpenIddictConstants.Claims.Name,
            user.UserName);
        if (user.Email is not null)
        {
            _ = identity.SetClaim(
                OpenIddictConstants.Claims.Email,
                user.Email);
        }

        _ = identity.SetDestinations(static claim =>
            claim.Type switch
            {
                OpenIddictConstants.Claims.Name
                    when claim.Subject?.HasScope(OpenIddictConstants.Scopes.Profile) is true
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                OpenIddictConstants.Claims.Email
                    when claim.Subject?.HasScope(OpenIddictConstants.Scopes.Email) is true
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                _ => [OpenIddictConstants.Destinations.AccessToken],
            });

        _ = identity.SetScopes(
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            IdentityHostingExtensions.ApiScope);
        _ = identity.SetResources(IdentityHostingExtensions.Audience);

        return SignIn(
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private IActionResult RedirectToFrontendLogin(
        OpenIddictRequest request,
        bool error)
    {
        if (!TryGetFrontendLoginUri(request, out Uri? callbackUri)
            || request.ClientId is not { Length: > 0 and <= 256 } clientId
            || clientId.AsSpan().IndexOfAny('\r', '\n') >= 0
            || request.RequestUri is not { Length: > 0 and <= 2048 } requestUri
            || requestUri.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return BadRequest("Invalid authorization request.");
        }

        Uri callback = callbackUri!;

        var parameters = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = clientId,
            ["request_uri"] = requestUri,
        };
        if (error)
        {
            parameters["error"] = "invalid_credentials";
        }

        string location = QueryHelpers.AddQueryString(
            callback.AbsoluteUri,
            parameters);
        return Redirect(location);
    }

    private static bool TryGetFrontendLoginUri(
        OpenIddictRequest request,
        out Uri? callbackUri)
    {
        callbackUri = null;
        if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out Uri? uri)
            || uri is null
            || uri.Fragment.Length > 0
            || uri.Query.Length > 0
            || uri.UserInfo.Length > 0
            || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (uri.AbsolutePath is not "/en/login" and not "/es/login")
        {
            return false;
        }

        callbackUri = uri;
        return true;
    }
}
