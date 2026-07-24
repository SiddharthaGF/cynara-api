using System.Diagnostics;

using Cynara.Api.Common.ActorContext;
using Cynara.Application;
using Cynara.Application.Failures;

using Microsoft.AspNetCore.Diagnostics;

namespace Cynara.Api.Common.ErrorHandling;

internal static class ExceptionHandlingExtensions
{
    public static WebApplication UseCynaraExceptionHandling(
        this WebApplication app)
    {
        _ = app.UseExceptionHandler(exceptionHandler =>
        {
            exceptionHandler.Run(async context =>
            {
                IExceptionHandlerFeature? feature = context.Features
                    .Get<IExceptionHandlerFeature>();
                Exception? error = feature?.Error;

                if (error is CynaraException cynaraException)
                {
                    IResult result = ProblemDetailsMapping.FromException(
                        cynaraException);
                    await result.ExecuteAsync(context).ConfigureAwait(false);
                    return;
                }

                if (error is not null)
                {
                    IFailureLogWriter writer = context.RequestServices
                        .GetRequiredService<IFailureLogWriter>();
                    await writer.RecordAsync(
                        error,
                        BuildFailureRequestContext(context),
                        StatusCodes.Status500InternalServerError,
                        context.RequestAborted).ConfigureAwait(false);
                }

                string detail = error is null
                    ? "An unexpected error occurred."
                    : "An unexpected error occurred. See the failure log for details.";

                await ProblemDetailsMapping.Unexpected(detail)
                    .ExecuteAsync(context)
                    .ConfigureAwait(false);
            });
        });

        return app;
    }

    private static FailureRequestContext BuildFailureRequestContext(
        HttpContext context)
    {
        HttpRequest request = context.Request;
        return new FailureRequestContext(
            Method: request.Method,
            Path: request.Path.HasValue ? request.Path.Value : null,
            Query: request.QueryString.HasValue ? request.QueryString.Value : null,
            ActorId: context.GetActorId(),
            TraceId: Activity.Current?.TraceId.ToString());
    }
}
