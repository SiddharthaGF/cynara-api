namespace Cynara.Infrastructure.Modules.Identity;

internal static class WebLoginRedirectUriBuilder
{
    /// <summary>
    /// Derives <c>/en/login</c> and <c>/es/login</c> URIs from each valid
    /// http(s) origin; invalid entries are skipped instead of throwing so a
    /// single bad variable cannot break startup.
    /// </summary>
    public static IReadOnlyList<string> Build(
        IEnumerable<string?>? origins)
    {
        List<string> uris = [];
        foreach (string? origin in origins ?? [])
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? parsed)
                || (!string.Equals(
                        parsed.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.Ordinal)
                    && !string.Equals(
                        parsed.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.Ordinal)))
            {
                continue;
            }

            string authority = parsed.GetLeftPart(UriPartial.Authority);
            uris.Add($"{authority}/en/login");
            uris.Add($"{authority}/es/login");
        }

        return uris;
    }
}
