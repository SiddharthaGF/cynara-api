using Cynara.Application;

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

                if (feature?.Error is CynaraException cynaraException)
                {
                    IResult result = ProblemDetailsMapping.FromException(
                        cynaraException);
                    await result.ExecuteAsync(context).ConfigureAwait(false);
                    return;
                }

                await Results.Problem(
                    detail: feature?.Error.Message,
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Unexpected error").ExecuteAsync(context).ConfigureAwait(false);
            });
        });

        return app;
    }
}
