namespace Cynara.Api.Hosting;

internal static class QueryCountingMiddlewareExtensions
{
    public static IApplicationBuilder UseQueryCounting(
        this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<QueryCountingMiddleware>();
    }
}
