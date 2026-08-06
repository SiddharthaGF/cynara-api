using Cynara.Application.Modules.Hospitals;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

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
                StringComparison.Ordinal) is true)
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
