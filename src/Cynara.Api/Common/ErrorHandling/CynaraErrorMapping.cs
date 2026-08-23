using Cynara.Application;

namespace Cynara.Api.Common.ErrorHandling;

/// <summary>
/// Pointer pair shared by both transports: the JsonAPI pipeline emits the
/// bare field path; the minimal-API envelope prefixes it with
/// <c>/data/attributes/answersJson/</c> so <c>source.pointer</c> traces back
/// to the answers attribute. Each transport picks the pointer it needs.
/// </summary>
internal sealed record CynaraErrorSource(string JsonApiPointer, string MinimalApiPointer);

/// <summary>
/// One error in a JSON:API <c>errors</c> array, mirroring the JSON:API error
/// object fields. <see cref="Title"/> repeats the document-level title so the
/// envelope shape stays identical across both transports.
/// </summary>
internal sealed record CynaraErrorItem(
    string? Code,
    string Title,
    string Detail,
    CynaraErrorSource? Source);

/// <summary>
/// Wire-neutral JSON:API error document. <see cref="StatusCode"/> is the
/// envelope-level HTTP status (a single response shares one status code);
/// each <see cref="Items"/> entry mirrors <see cref="Title"/>.
/// </summary>
internal sealed record CynaraErrorDocument(
    int StatusCode,
    string Title,
    IReadOnlyList<CynaraErrorItem> Items);

/// <summary>
/// Neutral JSON:API error document shared by the minimal-API error handler
/// (<see cref="ProblemDetailsMapping"/>) and the JsonAPI pipeline's exception
/// handler so both emit byte-identical responses for a given exception;
/// form-response validation errors map to one item per field error.
/// </summary>
internal static class CynaraErrorMapping
{
    private const string UnexpectedTitle = "Unexpected error";
    private const string ValidationTitle = "Validation failed";
    private const string MinimalApiAnswersPrefix = "/data/attributes/answersJson/";

    public static CynaraErrorDocument FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            FormResponseValidationException formResponse =>
                BuildFormResponseDocument(formResponse),
            CynaraException cynara => BuildSingleItem(
                (int)cynara.StatusCode,
                cynara.Title,
                cynara.Message),
            _ => BuildSingleItem(
                StatusCodes.Status500InternalServerError,
                UnexpectedTitle,
                exception.Message),
        };
    }

    public static CynaraErrorDocument Unexpected(string detail)
    {
        return BuildSingleItem(
            StatusCodes.Status500InternalServerError,
            UnexpectedTitle,
            detail);
    }

    private static CynaraErrorDocument BuildSingleItem(
        int statusCode,
        string title,
        string detail)
    {
        return new CynaraErrorDocument(
            statusCode,
            title,
            [new CynaraErrorItem(Code: null, Title: title, Detail: detail, Source: null)]);
    }

    private static CynaraErrorDocument BuildFormResponseDocument(
        FormResponseValidationException exception)
    {
        CynaraErrorItem[] items = [.. exception.Errors
            .Select(static error => new CynaraErrorItem(
                Code: error.Code,
                Title: ValidationTitle,
                Detail: error.Message,
                Source: BuildFormResponseSource(error.Path)))];

        return new CynaraErrorDocument(
            StatusCodes.Status400BadRequest,
            ValidationTitle,
            items);
    }

    private static CynaraErrorSource? BuildFormResponseSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return new CynaraErrorSource(
            JsonApiPointer: path,
            MinimalApiPointer: MinimalApiAnswersPrefix + path);
    }
}
