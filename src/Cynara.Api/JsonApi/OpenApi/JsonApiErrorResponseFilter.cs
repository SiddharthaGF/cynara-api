using System.Globalization;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Attaches the reusable <c>JsonApiErrorDocument</c> schema to every error
/// response (400–500) lacking a documented body, so all errors resolve to
/// the shared contract schema instead of an untyped 200-only body.
/// </summary>
/// <remarks>
/// Responses that already carry a typed body (e.g.
/// <c>ProblemDetails</c> on the workspace PATCH 400) are left untouched.
/// </remarks>
public sealed class JsonApiErrorResponseFilter : IOperationFilter
{
    private const string JsonApiMediaType = "application/vnd.api+json";

    private static readonly HashSet<int> ErrorStatusCodes =
    [
        StatusCodes.Status400BadRequest,
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
        StatusCodes.Status500InternalServerError,
    ];

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Responses is null)
        {
            return;
        }

        foreach (int statusCode in ErrorStatusCodes)
        {
            string key = statusCode.ToString(CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(key, out IOpenApiResponse? response))
            {
                continue;
            }

            if (response is not OpenApiResponse openApiResponse
                || openApiResponse.Content is { Count: > 0 })
            {
                continue;
            }

            openApiResponse.Content = new Dictionary<string, OpenApiMediaType>(
                StringComparer.Ordinal)
            {
                [JsonApiMediaType] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchemaReference("jsonApiErrorDocument"),
                },
            };
        }
    }
}
