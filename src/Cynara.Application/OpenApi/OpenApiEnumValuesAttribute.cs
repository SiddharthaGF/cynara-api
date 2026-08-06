namespace Cynara.Application.OpenApi;

/// <summary>
/// Documents the allowed wire values for a DTO property that carries an
/// enum-like string (the Application layer maps domain enums to camelCase
/// strings before serialization). The API's <c>CynaraEnumSchemaFilter</c>
/// turns this into an OpenAPI string schema with a fixed list of allowed
/// values, so client generators can build typed unions instead of free-form
/// strings.
/// </summary>
/// <remarks>
/// Lives in <c>Cynara.Application</c> because the DTOs it annotates live
/// there; <c>Cynara.Api</c> (which references this project) reads it during
/// schema generation.
/// </remarks>
/// <param name="values">Allowed string values, in canonical wire order.</param>
[AttributeUsage(
    AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = true)]
public sealed class OpenApiEnumValuesAttribute(params string[] values)
    : Attribute
{
    /// <summary>The allowed wire values.</summary>
    public IReadOnlyList<string> AllowedValues { get; } = values;

    /// <summary>
    /// When <see langword="true"/>, the property may also be absent or
    /// <see langword="null"/> on the wire; the schema is marked nullable.
    /// </summary>
    public bool Nullable { get; init; }
}
