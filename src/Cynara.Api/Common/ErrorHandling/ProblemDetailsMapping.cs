using System.Text.Json;

using Cynara.Application;

namespace Cynara.Api.Common.ErrorHandling;

internal static class ProblemDetailsMapping
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IResult FromException(CynaraException exception)
    {
        return exception switch
        {
            NotFoundException => JsonApiError(
                StatusCodes.Status404NotFound,
                "Not found",
                exception.Message),
            ConflictException => JsonApiError(
                StatusCodes.Status409Conflict,
                "Conflict",
                exception.Message),
            ValidationException => JsonApiError(
                StatusCodes.Status400BadRequest,
                "Validation failed",
                exception.Message),
            ConcurrencyException => JsonApiError(
                StatusCodes.Status409Conflict,
                "Concurrency conflict",
                exception.Message),
            InvalidStateException => JsonApiError(
                StatusCodes.Status409Conflict,
                "Invalid state",
                exception.Message),
            FormResponseValidationException validationException =>
                FormResponseErrors(validationException),
            _ => JsonApiError(
                StatusCodes.Status500InternalServerError,
                "Unexpected error",
                exception.Message),
        };
    }

    public static IResult Unexpected(string detail)
    {
        return JsonApiError(
            StatusCodes.Status500InternalServerError,
            "Unexpected error",
            detail);
    }

    private static IResult FormResponseErrors(
        FormResponseValidationException exception)
    {
        var errors = exception.Errors.Select(static error => new
        {
            status = "400",
            code = error.Code,
            title = "Validation failed",
            detail = error.Message,
            source = new
            {
                pointer = string.IsNullOrWhiteSpace(error.Path)
                    ? null
                    : $"/data/attributes/answersJson/{error.Path}",
            },
        }).ToArray();

        return Results.Json(
            new { errors },
            SerializerOptions,
            contentType: "application/vnd.api+json",
            statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult JsonApiError(
        int statusCode,
        string title,
        string detail)
    {
        var document = new
        {
            errors = new[]
            {
                new
                {
                    status = statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    title,
                    detail,
                },
            },
        };

        return Results.Json(
            document,
            SerializerOptions,
            contentType: "application/vnd.api+json",
            statusCode: statusCode);
    }
}
