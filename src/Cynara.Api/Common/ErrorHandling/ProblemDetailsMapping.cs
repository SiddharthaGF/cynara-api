using Cynara.Application;

namespace Cynara.Api.Common.ErrorHandling;

internal static class ProblemDetailsMapping
{
    public static IResult FromException(CynaraException exception)
    {
        return exception switch
        {
            NotFoundException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found"),
            ConflictException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict"),
            ValidationException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation failed"),
            ConcurrencyException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Concurrency conflict"),
            InvalidStateException => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Invalid state"),
            FormResponseValidationException validationException => Results.Json(
                new
                {
                    title = "Validation failed",
                    status = StatusCodes.Status400BadRequest,
                    detail = validationException.Message,
                    errors = validationException.Errors.Select(error => new
                    {
                        error.Code,
                        error.Path,
                        error.Message,
                    }),
                },
                statusCode: StatusCodes.Status400BadRequest),
            _ => Results.Problem(
                detail: exception.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Unexpected error"),
        };
    }
}
