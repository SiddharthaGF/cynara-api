using System.Net;

using Cynara.Application;

using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Serialization.Objects;

namespace Cynara.Api.JsonApi;

/// <summary>
/// Maps application <see cref="CynaraException"/> types to JSON:API error
/// objects with the correct HTTP status instead of treating them as 500s.
/// </summary>
internal sealed class CynaraJsonApiExceptionHandler(
    ILoggerFactory loggerFactory,
    IJsonApiOptions options)
    : ExceptionHandler(loggerFactory, options)
{
    protected override IReadOnlyList<ErrorObject> CreateErrorResponse(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is FormResponseValidationException formResponseValidation)
        {
            return [.. formResponseValidation.Errors
                .Select(static error => new ErrorObject(HttpStatusCode.BadRequest)
                {
                    Code = error.Code,
                    Title = "Validation failed",
                    Detail = error.Message,
                    Source = string.IsNullOrWhiteSpace(error.Path)
                        ? null
                        : new ErrorSource { Pointer = error.Path },
                })];
        }

        if (exception is CynaraException cynaraException)
        {
            return
            [
                new ErrorObject(MapStatus(cynaraException))
                {
                    Title = MapTitle(cynaraException),
                    Detail = cynaraException.Message,
                },
            ];
        }

        return base.CreateErrorResponse(exception);
    }

    private static HttpStatusCode MapStatus(CynaraException exception)
    {
        return exception switch
        {
            NotFoundException => HttpStatusCode.NotFound,
            ConflictException => HttpStatusCode.Conflict,
            ValidationException => HttpStatusCode.BadRequest,
            ConcurrencyException => HttpStatusCode.Conflict,
            InvalidStateException => HttpStatusCode.Conflict,
            _ => HttpStatusCode.InternalServerError,
        };
    }

    private static string MapTitle(CynaraException exception)
    {
        return exception switch
        {
            NotFoundException => "Not found",
            ConflictException => "Conflict",
            ValidationException => "Validation failed",
            ConcurrencyException => "Concurrency conflict",
            InvalidStateException => "Invalid state",
            _ => "Unexpected error",
        };
    }
}
