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
/// Enriches OpenAPI info and reusable JSON:API error schemas for Scalar.
/// Kept conservative: heavy schema mutation can break JADNC OpenAPI generation.
/// </summary>
public sealed class CynaraOpenApiDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);
        swaggerDoc.Info ??= new OpenApiInfo();
        if (string.IsNullOrWhiteSpace(swaggerDoc.Info.Description))
        {
            swaggerDoc.Info.Description =
                "Cynara JSON:API. Media type application/vnd.api+json. "
                + "Mutating workflows accept rowVersion as a query parameter. "
                + "Non-resource Form AI: GET /api/ai/status and POST "
                + "/api/ai/forms/{id}/chat (application/json); POST "
                + ".../chat/stream (text/event-stream). Settings: "
                + "GET/PATCH /api/aiProviderSettings/{id}.";
        }
        else if (!swaggerDoc.Info.Description.Contains(
                     "page[size]",
                     StringComparison.Ordinal))
        {
            swaggerDoc.Info.Description +=
                " Query: filter, sort, include, fields[type], page[number], "
                + "page[size] (default 20, max 100). Include depth max 3.";
        }

        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        if (!swaggerDoc.Components.Schemas.ContainsKey("JsonApiErrorObject"))
        {
            swaggerDoc.Components.Schemas["JsonApiErrorObject"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description = "JSON:API error object.",
                Properties = new Dictionary<string, IOpenApiSchema>(
                    StringComparer.Ordinal)
                {
                    ["id"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["status"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "HTTP status as a string, e.g. \"409\".",
                    },
                    ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["source"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = new Dictionary<string, IOpenApiSchema>(
                            StringComparer.Ordinal)
                        {
                            ["pointer"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                            },
                            ["parameter"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                            },
                        },
                    },
                },
            };
        }

        if (!swaggerDoc.Components.Schemas.ContainsKey("JsonApiErrorDocument"))
        {
            swaggerDoc.Components.Schemas["JsonApiErrorDocument"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string>(StringComparer.Ordinal) { "errors" },
                Properties = new Dictionary<string, IOpenApiSchema>(
                    StringComparer.Ordinal)
                {
                    ["errors"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Array,
                        Items = new OpenApiSchemaReference("JsonApiErrorObject"),
                    },
                },
            };
        }

        // Minimal probe/root routes must not clutter Scalar.
        if (swaggerDoc.Paths is not null)
        {
            _ = swaggerDoc.Paths.Remove("/");
            _ = swaggerDoc.Paths.Remove("/health");
        }
    }
}
