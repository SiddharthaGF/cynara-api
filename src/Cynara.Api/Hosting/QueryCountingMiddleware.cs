using System.Globalization;

using Cynara.Infrastructure.Persistence.QueryCounting;

namespace Cynara.Api.Hosting;

/// <summary>
/// Surfaces the per-request read-command count as the <c>X-Query-Count</c>
/// response header and warns past <see cref="WarningThreshold"/> commands —
/// the classic fingerprint of an N+1 regression that integration tests can
/// fail on instead of merely alerting at runtime.
/// </summary>
internal sealed class QueryCountingMiddleware(
    RequestDelegate next,
    ILogger<QueryCountingMiddleware> logger)
{
    internal const int WarningThreshold = 50;

    /// <summary>
    /// Registers the header callback before awaiting the pipeline so it runs
    /// in OnStarting — after the handler and its queries have completed —
    /// making the final count observable even though the body streams.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        QueryCounter counter = context.RequestServices
            .GetRequiredService<QueryCounter>();

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
