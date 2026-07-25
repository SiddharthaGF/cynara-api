using Cynara.Application;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Cynara.Api.Tests.Failures;

public sealed class FailureTestEndpointsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            next(app);
            _ = app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    if (context.Request.Path.Equals(
                            "/test/throw-unhandled",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("boom-unhandled");
                    }

                    if (context.Request.Path.Equals(
                            "/test/throw-validation",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ValidationException("validation failure");
                    }
                }

                await nextMiddleware(context).ConfigureAwait(false);
            });
        };
    }
}
