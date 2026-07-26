using Cynara.Application;

namespace Cynara.Api.Common.ErrorHandling;

/// <summary>
/// Pointer pair shared by both JSON:API transports. The JsonAPI pipeline
/// emits the bare field path (e.g. <c>/fields/0</c>); the minimal-API
/// envelope prefixes it with <c>/data/attributes/answersJson/</c> so the
/// resulting <c>source.pointer</c> traces back to the answers attribute.
/// Both forms are kept side-by-side on the wire-neutral document so each
/// transport picks the one it needs.
/// </summary>
internal sealed record CynaraErrorSource(string JsonApiPointer, string MinimalApiPointer);

/// <summary>
/// One error in a JSON:API <c>errors</c> array. <see cref="Code"/>,
/// <see cref="Title"/>, <see cref="Detail"/>, and <see cref="Source"/> mirror
/// the JSON:API error-object fields. <see cref="Title"/> is repeated on each
/// item (the document-level <see cref="CynaraErrorDocument.Title"/> matches
/// it) to keep the envelope shape identical to the original transport-
/// specific envelopes.
/// </summary>
internal sealed record CynaraErrorItem(
    string? Code,
    string Title,
    string Detail,
    CynaraErrorSource? Source);

/// <summary>
/// Wire-neutral JSON:API error document. <see cref="StatusCode"/> is the
/// envelope-level HTTP status (mirroring the JSON:API convention that a
/// single response carries one status code). <see cref="Title"/> is the
/// envelope-level title; each <see cref="Items"/> entry mirrors it.
/// </summary>
internal sealed record CynaraErrorDocument(
    int StatusCode,
    string Title,
    IReadOnlyList<CynaraErrorItem> Items);

/// <summary>
/// Neutral JSON:API error document shared by both the minimal-API error
/// handler (<see cref="ProblemDetailsMapping"/>) and the JsonAPI pipeline's
/// exception handler (<c>CynaraJsonApiExceptionHandler</c>). Each transport
/// only wraps this document; the wire shape comes from here so both
/// transports emit byte-identical responses for the same exception.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="CynaraErrorDocument"/> carries the HTTP status code at the
/// envelope level (all errors in a single response share it, mirroring
/// JSON:API's specification) and emits one <see cref="CynaraErrorItem"/> per
/// concrete error. For <see cref="FormResponseValidationException"/> the
/// mapping produces one item per field error so each carries its own code
/// and pointer; for every other <see cref="CynaraException"/> it produces a
/// single item with <see cref="CynaraErrorItem.Code"/> and
/// <see cref="CynaraErrorItem.Source"/> both <see langword="null"/>.
/// </para>
/// <para>
/// <see cref="CynaraErrorSource"/> keeps both pointer forms available
/// because the two transports historically emitted different shapes. Each
/// transport picks the one it needs and ignores the other.
/// </para>
/// </remarks>
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
