using System.Security.Claims;
using System.Text.Encodings.Web;

using Cynara.Api.Common.ActorContext;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cynara.Api.Tests.Support;

/// <summary>
/// Authentication scheme name used by the F1 test seam. The default test
/// factory replaces the OpenIddict validation scheme with this handler so
/// every header-based request is treated as authenticated, keeping the
/// existing <c>X-Actor-Id</c> actor suites running without real tokens. Auth
/// suites disable the seam and use genuine OpenIddict tokens instead.
/// </summary>
internal static class TestAuthenticationDefaults
{
    public const string Scheme = "Test";
}

/// <summary>
/// Forces the authentication defaults to the test scheme regardless of the
/// order in which the host's own authentication registration runs. The
/// integration factory is configured before <c>Program.cs</c> executes, so a
/// plain <c>AddAuthentication</c> here would be overridden by the host's
/// OpenIddict defaults; a post-configure runs after every configure action.
/// </summary>
internal sealed class TestAuthenticationPostConfigure : IPostConfigureOptions<AuthenticationOptions>
{
    public void PostConfigure(string? name, AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.DefaultAuthenticateScheme = TestAuthenticationDefaults.Scheme;
        options.DefaultScheme = TestAuthenticationDefaults.Scheme;
        options.DefaultChallengeScheme = TestAuthenticationDefaults.Scheme;
        options.DefaultForbidScheme = TestAuthenticationDefaults.Scheme;
    }
}

/// <summary>
/// Test-authentication handler that marks every request as authenticated.
/// The seam actor identity still comes from <see cref="CurrentActor"/> (the
/// <c>X-Actor-Id</c> header), so capability and audit behavior is unchanged.
/// </summary>
internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#pragma warning disable CS0618 // Test handler must use the (obsolete) ISystemClock base ctor.
    public TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }
#pragma warning restore CS0618

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Name, "test-user"),
                new Claim("sub", "test-user"),
            ],
            Scheme.Name);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(
            principal,
            Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
