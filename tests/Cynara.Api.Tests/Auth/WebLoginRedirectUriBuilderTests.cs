using Cynara.Infrastructure.Modules.Identity;

namespace Cynara.Api.Tests.Auth;

public sealed class WebLoginRedirectUriBuilderTests
{
    [Fact]
    public void Build_NullOrEmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(WebLoginRedirectUriBuilder.Build(origins: null));
        Assert.Empty(
            WebLoginRedirectUriBuilder.Build([string.Empty, "   "]));
    }

    [Fact]
    public void Build_ValidOrigins_DerivesLocalizedLoginPaths()
    {
        IReadOnlyList<string> uris = WebLoginRedirectUriBuilder.Build([
            "https://pr-42-fix-login-cynara-web.livesanty.workers.dev",
        ]);

        Assert.Equal(
        [
            "https://pr-42-fix-login-cynara-web.livesanty.workers.dev/en/login",
            "https://pr-42-fix-login-cynara-web.livesanty.workers.dev/es/login",
        ],
        uris);
    }

    [Fact]
    public void Build_TrailingSlashAndNonHttpSchemes_AreNormalizedOrSkipped()
    {
        IReadOnlyList<string> uris = WebLoginRedirectUriBuilder.Build([
            "https://abc123-cynara-web.livesanty.workers.dev/",
            "ftp://files.example",
            "not-a-uri",
        ]);

        Assert.Equal(
        [
            "https://abc123-cynara-web.livesanty.workers.dev/en/login",
            "https://abc123-cynara-web.livesanty.workers.dev/es/login",
        ],
        uris);
    }
}
