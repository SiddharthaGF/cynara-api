using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Cynara.IdentitySpike.Endpoints;

/// <summary>
/// OpenIddict token endpoint. Implements the password, refresh token, and
/// client credentials grants using the canonical OpenIddict
/// <c>SignIn</c>-based exchange pattern. Client authentication is performed
/// automatically by OpenIddict before this action runs.
/// </summary>
[ApiController]
public sealed class TokenController(
    UserManager<IdentityUser<Guid>> userManager,
    SignInManager<IdentityUser<Guid>> signInManager) : ControllerBase
{
    /// <summary>
    /// Handles <c>POST /connect/token</c> exchanges.
    /// </summary>
    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The request is not a valid OpenIddict request.");

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrantAsync(request).ConfigureAwait(false);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrantAsync().ConfigureAwait(false);
        }

        if (request.IsClientCredentialsGrantType())
        {
            return HandleClientCredentialsGrant(request);
        }

        return BadRequest(new OpenIddictResponse
        {
            Error = OpenIddictConstants.Errors.UnsupportedGrantType,
            ErrorDescription = "The specified grant type is not supported.",
        });
    }

    private async Task<IActionResult> HandlePasswordGrantAsync(
        OpenIddictRequest request)
    {
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
                    when claim.Subject?.HasScope(OpenIddictConstants.Scopes.Profile) == true
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                OpenIddictConstants.Claims.Email
                    when claim.Subject?.HasScope(OpenIddictConstants.Scopes.Email) == true
                    => [OpenIddictConstants.Destinations.AccessToken,
                        OpenIddictConstants.Destinations.IdentityToken],
                _ => [OpenIddictConstants.Destinations.AccessToken],
            });

        _ = identity.SetScopes(
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.OfflineAccess,
            "cynara_api");
        _ = identity.SetResources("cynara-api");

        return SignIn(
            new System.Security.Claims.ClaimsPrincipal(identity),
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

        // Keep the claims and scopes from the original token and stamp the
        // audience so the refreshed access token still targets the API.
        _ = info.Principal.SetResources("cynara-api");
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
        _ = identity.SetDestinations(static claim =>
            [OpenIddictConstants.Destinations.AccessToken]);
        _ = identity.SetScopes("cynara_api");
        _ = identity.SetResources("cynara-api");

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
