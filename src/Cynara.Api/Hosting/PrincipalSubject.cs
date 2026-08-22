using System.Security.Claims;

namespace Cynara.Api.Hosting;

internal static class PrincipalSubject
{
    public static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirst("sub")?.Value, out userId);
    }
}
