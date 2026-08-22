using System.Globalization;

using Cynara.Infrastructure.Persistence.QueryCounting;

namespace Cynara.Api.Hosting;

/// <summary>
/// Surfaces the per-request read-command count as the <c>X-Query-Count</c>
/// response header and logs a warning when a single request executes more
/// than <see cref="WarningThreshold"/> commands — the classic fingerprint of
/// an N+1 regression. The header keeps the count observable to integration
/// tests so a query-budget regression can fail the build, not just alert
/// at runtime.
/// </summary>
internal sealed class QueryCountingMiddleware(
    RequestDelegate next,
    ILogger<QueryCountingMiddleware> logger)
{
    internal const int WarningThreshold = 50;

    public async Task InvokeAsync(HttpContext context)
    {
        QueryCounter counter = context.RequestServices
            .GetRequiredService<QueryCounter>();

        // Register before awaiting the pipeline so the header is written in
        // OnStarting — which runs just before headers are sent, after the
        // handler (and its queries) have completed. Setting it here is what
        // makes the count observable even though the response body streams.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Query-Count"] = counter.Count.ToString(
                CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        await next(context).ConfigureAwait(false);

        if (counter.Count > WarningThreshold)
        {
            logger.LogWarning(
                "{Path} executed {Count} SQL read commands",
                context.Request.Path,
                counter.Count);
        }
    }
}
