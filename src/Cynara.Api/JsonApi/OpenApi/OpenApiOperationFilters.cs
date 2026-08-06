using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Documents the optional X-Actor-Id header used for audit attribution.
/// </summary>
public sealed class ActorIdOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);

        operation.Parameters ??= [];
        if (!operation.Parameters.Any(parameter =>
                string.Equals(
                    parameter.Name,
                    "X-Actor-Id",
                    StringComparison.OrdinalIgnoreCase)))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-Actor-Id",
                In = ParameterLocation.Header,
                Required = false,
                Description =
                    "Optional actor identity recorded on mutating workflows and "
                    + "audit events. Not an authentication gate in this maquette.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("ActorId")] = [],
        });
    }
}

/// <summary>
/// Documents the required X-Hospital-Code header used to resolve the tenant
/// workspace for every tenant-owned request. Skipped for exempt paths
/// (health, swagger, scalar) where the host middleware short-circuits.
/// </summary>
public sealed class HospitalCodeOperationFilter : IOperationFilter
{
    private static readonly HashSet<string> ExemptPathPrefixes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/swagger",
        "/scalar",
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        string? path = context.ApiDescription.RelativePath;
        if (path is null || ExemptPathPrefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters ??= [];
        if (operation.Parameters.Any(parameter =>
                string.Equals(
                    parameter.Name,
                    "X-Hospital-Code",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Hospital-Code",
            In = ParameterLocation.Header,
            Required = true,
            Description =
                "Required hospital workspace code. Selects the tenant scope "
                + "for the request. Unknown, missing, or inactive codes are "
                + "rejected before any workflow runs. The tenant context is "
                + "resolved by the host middleware; clients cannot override "
                + "it through request bodies or relationship data. "
                + "Examples: 'default', 'hospital-norte', 'hospital-sur'.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Pattern = "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$",
                MinLength = 1,
                MaxLength = 64,
            },
        });

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("HospitalCode")] = [],
        });
    }
}

/// <summary>
/// Adds the PATCH /api/workspace request body schema to the OpenAPI
/// operation. The controller binds the body manually via JsonSerializer,
/// so Swashbuckle cannot infer the schema from action parameters.
/// </summary>
public sealed class WorkspaceOperationFilter : IOperationFilter
{
    private const string WorkspacePath = "/api/workspace";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        string? path = context.ApiDescription.RelativePath;
        if (!string.Equals(
                path,
                WorkspacePath.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                context.ApiDescription.HttpMethod,
                "PATCH",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        IOpenApiRequestBody body = operation.RequestBody ??= new OpenApiRequestBody();
        body.Description =
            "PATCH body for /api/workspace. Only mutable display fields and "
            + "a concurrency token are accepted.";

        if (body is OpenApiRequestBody concrete)
        {
            concrete.Content ??=
                new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal);
            concrete.Content["application/vnd.api+json"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchemaReference("updateHospitalWorkspaceRequest"),
            };
        }
    }
}

/// <summary>
/// Documents the <c>text/event-stream</c> media type on the Form AI SSE
/// operation. The action writes events straight to the response body, so
/// Swashbuckle cannot infer a response schema or content type from a typed
/// result; the streaming contract is still part of the Stage 2 surface.
/// Error responses are JSON:API error documents (see
/// <see cref="JsonApiErrorResponseFilter"/>), so the SSE content inferred
/// from the action-level <c>[Produces]</c> is corrected to the shared error
/// schema.
/// </summary>
public sealed class FormAiStreamOperationFilter : IOperationFilter
{
    private const string StreamPath = "/api/ai/forms/{formDefinitionId}/chat/stream";
    private const string EventStreamMediaType = "text/event-stream";
    private const string JsonApiMediaType = "application/vnd.api+json";

    private static readonly HashSet<string> ErrorStatusCodes = new(
        StringComparer.Ordinal)
    {
        "400",
        "401",
        "403",
        "404",
        "409",
        "422",
        "500",
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        string? path = context.ApiDescription.RelativePath;
        if (!string.Equals(
                path,
                StreamPath.TrimStart('/'),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                context.ApiDescription.HttpMethod,
                "POST",
                StringComparison.OrdinalIgnoreCase)
            || operation.Responses is null)
        {
            return;
        }

        if (operation.Responses.TryGetValue("200", out IOpenApiResponse? success)
            && success is OpenApiResponse successResponse
            && (successResponse.Content is null || successResponse.Content.Count == 0))
        {
            successResponse.Description = "Server-Sent Events stream of Form AI "
                + "authoring events; one JSON event per SSE data frame.";
            successResponse.Content = new Dictionary<string, OpenApiMediaType>(
                StringComparer.Ordinal)
            {
                [EventStreamMediaType] = new OpenApiMediaType(),
            };
        }

        foreach (string statusCode in ErrorStatusCodes)
        {
            if (!operation.Responses.TryGetValue(
                    statusCode,
                    out IOpenApiResponse? errorResponse)
                || errorResponse is not OpenApiResponse openApiResponse
                || openApiResponse.Content is null
                || openApiResponse.Content.Count == 0
                || openApiResponse.Content.Keys.Any(key => !string.Equals(
                    key,
                    EventStreamMediaType,
                    StringComparison.Ordinal)))
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
