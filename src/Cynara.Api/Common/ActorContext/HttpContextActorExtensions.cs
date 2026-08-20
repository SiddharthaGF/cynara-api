using Cynara.Application.Modules.Capabilities;

namespace Cynara.Api.Common.ActorContext;

internal static class HttpContextActorExtensions
{
    /// <summary>
    /// Returns the actor identity resolved for the current request. This reads
    /// the scoped <see cref="ICurrentActor"/> — which the host registers as
    /// <see cref="PrincipalCurrentActor"/> in production (membership-resolved)
    /// and as the header-backed <see cref="CurrentActor"/> in the test seam —
    /// so audit and lifecycle attribution can never be set by a client-supplied
    /// <c>X-Actor-Id</c> header in production. Returns <see langword="null"/>
    /// when no request or no actor is resolved.
    /// </summary>
    public static string? GetActorId(this HttpContext httpContext)
    {
        return httpContext.RequestServices?.GetService<ICurrentActor>()?.ActorId;
    }

    /// <summary>
    /// Raw <c>X-Actor-Id</c> header value. Used only by the test seam's
    /// header-backed <see cref="CurrentActor"/>; production must never read
    /// this header for identity or audit attribution.
    /// </summary>
    public static string? GetActorIdFromHeader(this HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue(
                "X-Actor-Id",
                out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
