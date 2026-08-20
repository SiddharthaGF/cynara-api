namespace Cynara.Api.Hosting;

/// <summary>
/// Shared classification of request paths for tenant and actor resolution.
/// Both <see cref="HospitalContextMiddleware"/> and
/// <see cref="MembershipResolutionMiddleware"/> consult this policy so login,
/// discovery, schema, probe, and documentation traffic is never rejected
/// because it lacks a hospital header or a resolved user membership.
/// <see cref="IsTenantExemptPath"/> additionally marks the bearer-only
/// membership listing, which requires authentication but no hospital context.
/// </summary>
internal static class AuthPathPolicy
{
    private static readonly string[] PublicPaths =
    [
        "/",
        "/health",
        "/schemas",
        "/swagger",
        "/scalar",
        "/connect",
        "/.well-known",
    ];

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> is public
    /// and must not be gated by hospital context or actor membership.
    /// </summary>
    public static bool IsPublicPath(PathString path)
    {
        return !path.HasValue
        || PublicPaths.Any(x => path == x || path.StartsWithSegments(x, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> is the
    /// membership listing, which is tenant-exempt rather than public: it
    /// still requires a valid bearer token, but it must not demand a hospital
    /// header or resolve an actor. Only this exact route is exempt; all other
    /// tenant-owned routes keep their hospital and membership gates.
    /// </summary>
    public static bool IsTenantExemptPath(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        string value = path.Value ?? string.Empty;
        return value.TrimEnd('/')
            .Equals("/api/me/hospitals", StringComparison.OrdinalIgnoreCase);
    }
}
