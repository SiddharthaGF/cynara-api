using System.Security.Claims;

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Cynara.IdentitySpike.Endpoints;

/// <summary>
/// Interactive authorization endpoint for the disposable spike. Serves
/// <c>GET /connect/authorize</c> (renders a minimal HTML login form) and
/// <c>POST /connect/authorize</c> (validates the seed credentials and, on
/// success, signs the principal in through the OpenIddict server scheme so
/// the authorization-code grant completes and redirects back to the web
/// client with <c>?code=&amp;state=</c>).
///
/// This is the Web spike prerequisite: it lets Cynara Web validate the
/// authorization-code + PKCE flow against the OpenIddict server. The client
/// is registered with <c>ConsentTypes.Implicit</c>, so a successful sign-in
/// grants consent implicitly — no separate consent screen in this spike.
/// </summary>
[ApiController]
public sealed class AuthorizeController(
    UserManager<IdentityUser<Guid>> userManager,
    SignInManager<IdentityUser<Guid>> signInManager) : ControllerBase
{
    /// <summary>
    /// Handles the interactive authorization endpoint. GET renders the login
    /// form; POST validates credentials and completes the flow.
    /// </summary>
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeAsync()
    {
        OpenIddictRequest request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException(
                "The request is not a valid OpenIddict request.");

        // POST carries the form fields (email/password plus the hidden
        // authorization request). Validate the credentials and complete.
        if (HttpMethods.IsPost(Request.Method))
        {
            string? email = Request.Form["email"].ToString();
            string? password = Request.Form["password"].ToString();

            IdentityUser<Guid>? user = await userManager.FindByNameAsync(
                userName: email ?? string.Empty).ConfigureAwait(false);
            if (user is null)
            {
                return LoginForm(
                    request,
                    error: "Unknown user or invalid password.");
            }

            Microsoft.AspNetCore.Identity.SignInResult result =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    password ?? string.Empty,
                    lockoutOnFailure: false).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return LoginForm(
                    request,
                    error: "Unknown user or invalid password.");
            }

            return CompleteAuthorization(user);
        }

        // GET: render the login form, echoing the authorization request
        // parameters as hidden fields so the POST round-trip preserves them.
        return LoginForm(request, null);
    }

    /// <summary>
    /// Builds a principal for the seed user and signs it in through the
    /// OpenIddict server scheme, which completes the code grant.
    /// </summary>
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
            new ClaimsPrincipal(identity),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Renders a minimal login form. The OpenIddict request parameters are
    /// echoed back as hidden fields so the POST completes the same
    /// authorization request.
    /// </summary>
    private ContentResult LoginForm(
        OpenIddictRequest request,
        string? error)
    {
        string hidden = string.Join(
            Environment.NewLine,
            new[]
            {
                Hidden("client_id", request.ClientId),
                Hidden("redirect_uri", request.RedirectUri),
                Hidden("response_type", request.ResponseType),
                Hidden("scope", request.Scope),
                Hidden("state", request.State),
                Hidden("code_challenge", request.CodeChallenge),
                Hidden("code_challenge_method", request.CodeChallengeMethod),
            });

        string errorBlock = error is null
            ? string.Empty
            : $"<p class=\"error\">{System.Net.WebUtility.HtmlEncode(error)}</p>";

        string html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Cynara Identity Spike - Sign in</title>
              <style>
                body { font-family: system-ui, sans-serif; display: grid;
                       place-items: center; min-height: 100vh; margin: 0;
                       background: #f6f7f9; color: #17202a; }
                form { background: #fff; padding: 2rem 2.5rem; border-radius: 12px;
                       box-shadow: 0 1px 3px rgba(0,0,0,.12); width: 20rem; }
                h1 { font-size: 1.15rem; margin: 0 0 1rem; }
                label { display: block; margin: .75rem 0 .25rem; font-size: .875rem; }
                input { width: 100%; box-sizing: border-box; padding: .5rem .6rem;
                        border: 1px solid #d4d9e0; border-radius: 8px; }
                button { width: 100%; margin-top: 1.25rem; padding: .6rem;
                         border: 0; border-radius: 8px; background: #2563eb;
                         color: #fff; font-weight: 600; cursor: pointer; }
                .error { color: #b91c1c; font-size: .8125rem; margin: .5rem 0 0; }
              </style>
            </head>
            <body>
              <form method="post" action="/connect/authorize">
                <h1>Cynara - Sign in</h1>
                __HIDDEN__
                <label for="email">Email</label>
                <input id="email" name="email" type="text"
                       autocomplete="username" autofocus required>
                <label for="password">Password</label>
                <input id="password" name="password" type="password"
                       autocomplete="current-password" required>
                __ERROR__
                <button type="submit">Continue</button>
                <p style="font-size:.75rem;color:#6b7280;margin-top:1rem;">
                  Seed: doctor@cynara.dev / Cynara!Dev123</p>
              </form>
            </body>
            </html>
            """;

        return Content(
            html
                .Replace("__HIDDEN__", hidden, StringComparison.Ordinal)
                .Replace("__ERROR__", errorBlock, StringComparison.Ordinal),
            "text/html; charset=utf-8");
    }

    private static string Hidden(string name, string? value)
    {
        return $"""
            <input type="hidden" name="{name}"
                   value="{System.Net.WebUtility.HtmlEncode(value ?? string.Empty)}">
            """;
    }
}
