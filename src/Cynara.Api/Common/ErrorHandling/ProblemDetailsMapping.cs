using System.Globalization;
using System.Text.Json;

using Cynara.Application;

namespace Cynara.Api.Common.ErrorHandling;

/// <summary>
/// Minimal-API error envelope that wraps the shared
/// <see cref="CynaraErrorDocument"/> produced by <see cref="CynaraErrorMapping"/>.
/// Status codes, titles, details, codes, and pointers all originate in the
/// neutral mapping; this class only chooses the envelope shape (camelCase
/// property names, <c>application/vnd.api+json</c> content type) and the
/// transport-specific pointer form for <see cref="FormResponseValidationException"/>.
/// </summary>
internal static class ProblemDetailsMapping
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IResult FromException(CynaraException exception)
    {
        CynaraErrorDocument document = CynaraErrorMapping.FromException(exception);
        return BuildEnvelope(document);
    }

    public static IResult Unexpected(string detail)
    {
        return BuildEnvelope(CynaraErrorMapping.Unexpected(detail));
    }

    private static IResult BuildEnvelope(CynaraErrorDocument document)
    {
        object[] errors = [.. document.Items
            .Select(item => item switch
            {
                { Code: null, Source: null } => (object)new
                {
                    status = document.StatusCode.ToString(CultureInfo.InvariantCulture),
                    title = item.Title,
                    detail = item.Detail,
                },
                _ => new
                {
                    status = document.StatusCode.ToString(CultureInfo.InvariantCulture),
                    code = item.Code,
                    title = item.Title,
                    detail = item.Detail,
                    source = new { pointer = item.Source!.MinimalApiPointer },
                },
            })];

        return Results.Json(
            new { errors },
            SerializerOptions,
            contentType: "application/vnd.api+json",
            statusCode: document.StatusCode);
    }
}
