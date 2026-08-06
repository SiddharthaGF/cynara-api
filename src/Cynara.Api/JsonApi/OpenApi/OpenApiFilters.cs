using Cynara.Application.Modules.Hospitals;

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
/// Enriches OpenAPI info and reusable JSON:API error schemas for Scalar.
/// Kept conservative: heavy schema mutation can break JADNC OpenAPI generation.
/// </summary>
public sealed class CynaraOpenApiDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);

        SetInfoDescription(swaggerDoc);
        RegisterWorkspaceSchemas(swaggerDoc);
        RegisterContractSchemas(swaggerDoc);
        RemoveProbePaths(swaggerDoc);
    }

    private static void RegisterContractSchemas(OpenApiDocument swaggerDoc)
    {
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        // Reusable error/pagination shapes, keyed by the same camelCase ids
        // Swashbuckle generates for the CLR contract types so there is always
        // exactly one schema per concept. Swashbuckle normally emits these from
        // the [ProducesResponseType] references below; these fallbacks guarantee
        // the shapes exist even if an endpoint stops referencing them directly.
        IDictionary<string, IOpenApiSchema> schemas = swaggerDoc.Components.Schemas;
        TryRegister(schemas, "jsonApiErrorDocument", BuildJsonApiErrorDocumentSchema);
        TryRegister(schemas, "jsonApiError", BuildJsonApiErrorSchema);
        TryRegister(schemas, "jsonApiErrorSource", BuildJsonApiErrorSourceSchema);
        TryRegister(schemas, "paginationMeta", BuildPaginationMetaSchema);

        // Share the reusable pagination schema with the patient list response
        // instead of duplicating the three counters inline. JADNC removes
        // component schemas with no references, so a real reference keeps the
        // pagination shape in the committed contract.
        SharePaginationMetaWithPatientList(schemas);
    }

    private static void SharePaginationMetaWithPatientList(
        IDictionary<string, IOpenApiSchema> schemas)
    {
        if (!schemas.TryGetValue("patientListResponse", out IOpenApiSchema? schema)
            || schema is not OpenApiSchema listSchema
            || listSchema.Properties is null
            || !listSchema.Properties.TryGetValue("patients", out IOpenApiSchema? patients))
        {
            return;
        }

        OpenApiSchema secondPart = new()
        {
            Type = JsonSchemaType.Object,
            Description = listSchema.Description,
            AdditionalPropertiesAllowed = false,
            Required = new HashSet<string>(StringComparer.Ordinal) { "patients" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["patients"] = patients,
            },
        };

        listSchema.AllOf =
        [
            new OpenApiSchemaReference("paginationMeta"),
            secondPart,
        ];
        listSchema.Properties =
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        listSchema.Type = null;
        listSchema.Description = null;
        listSchema.AdditionalPropertiesAllowed = true;
    }

    private static void TryRegister(
        IDictionary<string, IOpenApiSchema> schemas,
        string name,
        Func<OpenApiSchema> builder)
    {
        if (!schemas.ContainsKey(name))
        {
            schemas[name] = builder();
        }
    }

    private static OpenApiSchema BuildJsonApiErrorDocumentSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description =
                "JSON:API error document. The top-level errors array carries "
                + "one object per error; a single response shares one HTTP "
                + "status code across all items.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "errors" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["errors"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Description = "One or more error objects.",
                    Items = new OpenApiSchemaReference("JsonApiError"),
                },
            },
        };
    }

    private static OpenApiSchema BuildJsonApiErrorSchema()
    {
        OpenApiSchema code = new()
        {
            Type = JsonSchemaType.String,
            Description = "Machine-readable error code, when available.",
        };

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal)
            {
                "status", "title", "detail",
            },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["status"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "HTTP status code as a string, e.g. \"400\".",
                },
                ["code"] = code,
                ["title"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Short human-readable summary of the problem.",
                },
                ["detail"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description =
                        "Human-readable explanation specific to this occurrence.",
                },
                ["source"] = new OpenApiSchemaReference("jsonApiErrorSource"),
            },
        };
    }

    private static OpenApiSchema BuildJsonApiErrorSourceSchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["pointer"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "JSON pointer to the offending request field.",
                },
            },
        };
    }

    private static OpenApiSchema BuildPaginationMetaSchema()
    {
        OpenApiSchema page = new()
        {
            Type = JsonSchemaType.Integer,
            Description = "1-based current page number.",
        };
        OpenApiSchema pageSize = new()
        {
            Type = JsonSchemaType.Integer,
            Description = "Number of items requested for the page.",
        };
        OpenApiSchema totalCount = new()
        {
            Type = JsonSchemaType.Integer,
            Description = "Total number of items matching the query.",
        };

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description =
                "Reusable pagination metadata for custom (non-JSON:API) list "
                + "responses such as the patient search result.",
            Required = new HashSet<string>(StringComparer.Ordinal)
            {
                "page", "pageSize", "totalCount",
            },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["page"] = page,
                ["pageSize"] = pageSize,
                ["totalCount"] = totalCount,
            },
        };
    }

    private static void RegisterWorkspaceSchemas(OpenApiDocument swaggerDoc)
    {
        swaggerDoc.Components ??= new OpenApiComponents();
        swaggerDoc.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);

        if (!swaggerDoc.Components.Schemas.ContainsKey("updateHospitalWorkspaceRequest"))
        {
            swaggerDoc.Components.Schemas["updateHospitalWorkspaceRequest"] =
                BuildUpdateHospitalWorkspaceRequestSchema();
        }
    }

    private static OpenApiSchema BuildUpdateHospitalWorkspaceRequestSchema()
    {
        OpenApiSchema name = new()
        {
            Type = JsonSchemaType.String,
            Description = "New human-readable workspace name. 1-256 characters.",
        };
        OpenApiSchema metadataJson = new()
        {
            Type = JsonSchemaType.String,
            Description = "Replacement metadata payload, or null to clear it.",
        };
        OpenApiSchema rowVersion = new()
        {
            Type = JsonSchemaType.Integer,
            Description = "Concurrency token from the last GET. Mismatch returns 409 Conflict.",
        };
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description =
                "PATCH /api/workspace body. Only mutable display fields and "
                + "a concurrency token are accepted. Hospital identifier, "
                + "code, and creation timestamp are immutable and rejected "
                + "with 400 if supplied.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "name", "rowVersion" },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["name"] = name,
                ["metadataJson"] = metadataJson,
                ["rowVersion"] = rowVersion,
            },
        };
    }

    private static void SetInfoDescription(OpenApiDocument swaggerDoc)
    {
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
    }

    private static void RemoveProbePaths(OpenApiDocument swaggerDoc)
    {
        if (swaggerDoc.Paths is not null)
        {
            _ = swaggerDoc.Paths.Remove("/");
            _ = swaggerDoc.Paths.Remove("/health");
        }
    }
}

/// <summary>
/// Enriches the workspace DTOs (read shape and PATCH body) with field-level
/// descriptions and exposes the request body schema explicitly under
/// <c>updateHospitalWorkspaceRequest</c>. Without this filter JsonApiDotNetCore
/// publishes the DTO as a JsonApi document wrapper with no inline field docs,
/// and the request body (which the controller binds manually via
/// <c>JsonSerializer</c>) does not surface as a referenced schema.
/// </summary>
public sealed class WorkspaceSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Type?.Equals(typeof(HospitalWorkspaceDto)) == true)
        {
            DescribeHospitalWorkspace(schema);
        }
        else if (context.Type?.Equals(typeof(UpdateHospitalWorkspaceRequest)) == true)
        {
            DescribeUpdateHospitalWorkspace(schema);
        }
    }

    private static void DescribeHospitalWorkspace(IOpenApiSchema schema)
    {
        schema.Description =
            "Resolved tenant workspace returned by GET /api/workspace. "
            + "Bound to the X-Hospital-Code header from the request context; "
            + "clients cannot select a different tenant.";

        SetDescription(
            schema,
            "id",
            "Surrogate Guid that identifies the hospital across the platform. Immutable.");
        SetDescription(
            schema,
            "code",
            "Stable business code used by clients and URLs. Immutable.",
            pattern: "^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$");
        SetDescription(
            schema,
            "name",
            "Human-readable workspace name shown in administrative UIs.");
        SetDescription(
            schema,
            "status",
            "Lifecycle status of the workspace. One of: active, inactive, suspended.");
        SetDescription(
            schema,
            "metadataJson",
            "Optional metadata payload stored as a JSON document.");
        SetDescription(
            schema,
            "rowVersion",
            "Optimistic concurrency token; required for PATCH updates.");
        SetDescription(
            schema,
            "createdAt",
            "UTC timestamp when the workspace was created. Immutable.",
            format: "date-time");
        SetDescription(
            schema,
            "updatedAt",
            "UTC timestamp of the last workspace metadata change.",
            format: "date-time");
    }

    private static void DescribeUpdateHospitalWorkspace(IOpenApiSchema schema)
    {
        schema.Description =
            "PATCH /api/workspace body. Only mutable display fields and a "
            + "concurrency token are accepted. Hospital identifier, code, and "
            + "creation timestamp are immutable and rejected with 400.";

        SetDescription(
            schema,
            "name",
            "New human-readable workspace name. 1-256 characters.");
        SetDescription(
            schema,
            "metadataJson",
            "Replacement metadata payload, or null to clear it.");
        SetDescription(
            schema,
            "rowVersion",
            "Concurrency token from the last GET. Mismatch returns 409 Conflict.");
    }

    private static void SetDescription(
        IOpenApiSchema schema,
        string propertyName,
        string description,
        string? pattern = null,
        string? format = null)
    {
        OpenApiSchema? property = TryGetProperty(schema, propertyName);
        if (property is null)
        {
            return;
        }

        property.Description = description;
        if (pattern is not null)
        {
            property.Pattern = pattern;
        }

        if (format is not null)
        {
            property.Format = format;
        }
    }

    private static OpenApiSchema? TryGetProperty(
        IOpenApiSchema schema, string propertyName)
    {
        if (schema.Properties is null)
        {
            return null;
        }

        return schema.Properties.TryGetValue(propertyName, out IOpenApiSchema? value)
            ? value as OpenApiSchema
            : null;
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
/// Marks the surrogate <c>id</c> property <c>readOnly: true</c> on schemas for
/// Application response DTOs. Request DTOs never declare an <c>id</c> field
/// (the identifier comes from the route), so this is safe to apply to every
/// Application schema that has one; JADNC resource and document schemas live
/// outside that namespace and keep their own conventions.
/// </summary>
public sealed class ReadOnlyIdSchemaFilter : ISchemaFilter
{
    private const string ApplicationModulesNamespace = "Cynara.Application.Modules";

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Type is null
            || schema is not OpenApiSchema openApiSchema
            || openApiSchema.Properties is null
            || !context.Type.Namespace?.StartsWith(
                ApplicationModulesNamespace,
                StringComparison.Ordinal) == true)
        {
            return;
        }

        foreach ((string name, IOpenApiSchema property) in openApiSchema.Properties)
        {
            if (string.Equals(name, "id", StringComparison.Ordinal)
                && property is OpenApiSchema idSchema)
            {
                idSchema.ReadOnly = true;
            }
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
