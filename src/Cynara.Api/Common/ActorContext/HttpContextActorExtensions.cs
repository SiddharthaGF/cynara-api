namespace Cynara.Api.Common.ActorContext;

internal static class HttpContextActorExtensions
{
    public static string? GetActorId(this HttpContext httpContext)
    {
        return httpContext.Request.Headers.TryGetValue(
                "X-Actor-Id",
                out Microsoft.Extensions.Primitives.StringValues value)
            ? value.ToString()
            : null;
    }
}
