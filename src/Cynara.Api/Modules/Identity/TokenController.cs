using Cynara.Api.Hosting;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Cynara.Api.Modules.Identity;

/// <summary>
/// OpenIddict token endpoint. Implements the authorization-code (+PKCE),
/// refresh token, client credentials, and (Development-only) password grants
/// using the canonical OpenIddict <c>SignIn</c>-based exchange pattern.
/// Client authentication is performed automatically by OpenIddict before this
/// action runs; revocation is handled by the OpenIddict server itself, so no
/// controller action is required for <c>POST /connect/revocation</c>.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("connect")]
public sealed class TokenController(
    UserManager<IdentityUser<Guid>> userManager,
    SignInManager<IdentityUser<Guid>> signInManager,
    IHostEnvironment environment) : ControllerBase
{
    /// <summary>Handles <c>POST /connect/token</c> exchanges.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the request is not a valid OpenIddict request.</exception>
    [HttpPost("token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException("The request is not a valid OpenIddict request.");

        return request.GrantType switch
        {
            OpenIddictConstants.GrantTypes.Password
                => await HandlePasswordGrantAsync(request),

            OpenIddictConstants.GrantTypes.AuthorizationCode
                => await HandleAuthorizationCodeGrantAsync(),

            OpenIddictConstants.GrantTypes.RefreshToken
                => await HandleRefreshTokenGrantAsync(),

            OpenIddictConstants.GrantTypes.ClientCredentials
                => HandleClientCredentialsGrant(request),

            _ => BadRequest(new OpenIddictResponse
            {
                Error = OpenIddictConstants.Errors.UnsupportedGrantType,
                ErrorDescription = "The specified grant type is not supported.",
            }),
        };
    }

    private async Task<IActionResult> HandlePasswordGrantAsync(
        OpenIddictRequest request)
    {
        // ROPC is unsafe for cynara-web and must never be available in
        // production; cynara-web uses authorization-code + PKCE exclusively.
        if (!environment.IsDevelopment())
        {
            return BadRequest(new OpenIddictResponse
            {
                Error = OpenIddictConstants.Errors.UnsupportedGrantType,
                ErrorDescription = "The password grant is not available.",
            });
        }

        IdentityUser<Guid>? user = await userManager.FindByNameAsync(
            request.Username ?? string.Empty).ConfigureAwait(false);
        if (user is null)
        {
            return InvalidCredentials();
        }

        Microsoft.AspNetCore.Identity.SignInResult result =
            await signInManager.CheckPasswordSignInAsync(
                user,
                request.Password ?? string.Empty,
                lockoutOnFailure: true).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        System.Security.Claims.ClaimsIdentity identity = new(
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
            new System.Security.Claims.ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Handles the authorization-code grant. OpenIddict has already validated
    /// the code, the PKCE verifier, and the client; re-issue the principal
    /// stored on the authorization so tokens are minted for the exact subject
    /// that completed the authorize step.
    /// </summary>
    private async Task<IActionResult> HandleAuthorizationCodeGrantAsync()
    {
        AuthenticateResult info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!info.Succeeded || info.Principal is null)
        {
            return BadRequest(new OpenIddictResponse
            {
                Error = OpenIddictConstants.Errors.InvalidGrant,
                ErrorDescription = "The authorization code is no longer valid.",
            });
        }

        _ = info.Principal.SetResources(IdentityHostingExtensions.Audience);
        return SignIn(
            info.Principal,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private async Task<IActionResult> HandleRefreshTokenGrantAsync()
    {
        AuthenticateResult info = await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!info.Succeeded || info.Principal is null)
        {
            return BadRequest(new OpenIddictResponse
            {
                Error = OpenIddictConstants.Errors.InvalidGrant,
                ErrorDescription = "The refresh token is no longer valid.",
            });
        }

        _ = info.Principal.SetResources(IdentityHostingExtensions.Audience);
        return SignIn(
            info.Principal,
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private Microsoft.AspNetCore.Mvc.SignInResult HandleClientCredentialsGrant(
        OpenIddictRequest request)
    {
        System.Security.Claims.ClaimsIdentity identity = new(
            authenticationType: TokenValidationParameters.DefaultAuthenticationType,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        _ = identity.SetClaim(
            OpenIddictConstants.Claims.Subject,
            request.ClientId);
        _ = identity.SetClaim(
            OpenIddictConstants.Claims.Name,
            request.ClientId);
        _ = identity.SetDestinations(static _ =>
            [OpenIddictConstants.Destinations.AccessToken]);
        _ = identity.SetScopes(IdentityHostingExtensions.ApiScope);
        _ = identity.SetResources(IdentityHostingExtensions.Audience);

        return SignIn(
            new System.Security.Claims.ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private BadRequestObjectResult InvalidCredentials()
    {
        return BadRequest(new OpenIddictResponse
        {
            Error = OpenIddictConstants.Errors.InvalidGrant,
            ErrorDescription = "Invalid credentials.",
        });
    }
}
