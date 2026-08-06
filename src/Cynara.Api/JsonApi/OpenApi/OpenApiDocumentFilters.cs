using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

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
