using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Cynara.Application.OpenApi;

using Microsoft.OpenApi;

using Swashbuckle.AspNetCore.SwaggerGen;

namespace Cynara.Api.JsonApi.OpenApi;

/// <summary>
/// Applies <see cref="OpenApiEnumValuesAttribute"/> to generated schemas so
/// that annotated string properties carry the allowed wire values as an
/// OpenAPI enum and are marked nullable when the attribute says the property
/// is optional.
/// </summary>
public sealed class CynaraEnumSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (schema is not OpenApiSchema openApiSchema
            || openApiSchema.Properties is null)
        {
            return;
        }

        foreach (PropertyInfo property in context.Type.GetProperties())
        {
            OpenApiEnumValuesAttribute? attribute = property
                .GetCustomAttributes(typeof(OpenApiEnumValuesAttribute), inherit: true)
                .OfType<OpenApiEnumValuesAttribute>()
                .SingleOrDefault();

            if (attribute is null)
            {
                continue;
            }

            string propertyName = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!openApiSchema.Properties.TryGetValue(propertyName, out IOpenApiSchema? value)
                || value is not OpenApiSchema enumSchema)
            {
                continue;
            }

            enumSchema.Type = attribute.Nullable
                ? JsonSchemaType.String | JsonSchemaType.Null
                : JsonSchemaType.String;
            enumSchema.Enum = [.. attribute.AllowedValues
                .Select(static item => JsonValue.Create(item))];
        }
    }
}
