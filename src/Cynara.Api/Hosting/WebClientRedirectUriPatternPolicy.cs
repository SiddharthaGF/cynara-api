using System.Globalization;

using System.Text.RegularExpressions;

namespace Cynara.Api.Hosting;

/// <summary>
/// Runtime acceptance of redirect URIs beyond the exact-match registration,
/// described purely by regular expressions bound from
/// <c>OpenIddict:WebClient:RedirectUriPatterns</c>. One anchored expression
/// covers both the stable production origin and the ephemeral preview
/// origins, for example:
/// <c>^https://(?:[a-z0-9][a-z0-9-]{0,62}\.)?cynara-web\.livesanty\.workers\.dev/(?:en|es)/login$</c>.
/// Switching hosting providers later is a configuration-only change.
/// Structural hygiene is always enforced on top of the patterns — HTTPS
/// only, default port, no userinfo, query, fragment, backslashes, or
/// trailing-dot hostnames — and matching runs against the parsed URI so a
/// lookalike authority cannot smuggle through the raw string.
/// </summary>
public sealed class WebClientRedirectUriPatternPolicy
{
    private const string ClientIdKey = "OpenIddict:WebClient:ClientId";

    private const string PatternsKey =
        "OpenIddict:WebClient:RedirectUriPatterns";

    private WebClientRedirectUriPatternPolicy(
        string clientId,
        IReadOnlyList<Regex> patterns)
    {
        ClientId = clientId;
        Patterns = patterns;
    }

    /// <summary>Client id the patterns are scoped to.</summary>
    public string ClientId { get; }

    private IReadOnlyList<Regex> Patterns { get; }

    /// <summary>
    /// Builds the policy from configuration, failing fast at startup when an
    /// expression cannot be compiled. An empty pattern list disables the
    /// feature entirely.
    /// </summary>
    public static WebClientRedirectUriPatternPolicy FromConfiguration(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string clientId = configuration[ClientIdKey] ?? "cynara-web";
        string[]? configured = configuration
            .GetSection(PatternsKey)
            .Get<string[]>();
        List<Regex> patterns = [];
        for (int index = 0; index < (configured ?? []).Length; index++)
        {
            string pattern = configured![index];
            try
            {
                var compiled = new Regex(
                    pattern,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1));
                patterns.Add(compiled);
            }
            catch (ArgumentException exception)
            {
                string message = "Invalid redirect URI pattern at "
                    + PatternsKey
                    + ":"
                    + index.ToString(CultureInfo.InvariantCulture)
                    + ": '"
                    + pattern
                    + "'.";
                throw new InvalidOperationException(message, exception);
            }
        }

        return new(clientId, patterns);
    }

    /// <summary>
    /// Determines whether the redirect URI belongs to the configured client
    /// and satisfies every pattern and structural constraint.
    /// </summary>
    public bool Matches(string? clientId, Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        if (Patterns.Count == 0
            || !string.Equals(
                clientId,
                ClientId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(
                redirectUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (redirectUri.OriginalString.Contains('\\'))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(redirectUri.UserInfo)
            || !redirectUri.IsDefaultPort
            || redirectUri.Query.Length > 0
            || redirectUri.Fragment.Length > 0)
        {
            return false;
        }

        string host = redirectUri.DnsSafeHost;
        if (host.Length == 0
            || host.EndsWith('.'))
        {
            return false;
        }

        string candidate =
            $"https://{host.ToLowerInvariant()}{redirectUri.AbsolutePath}";
        return Patterns.Any(pattern => pattern.IsMatch(candidate));
    }
}
