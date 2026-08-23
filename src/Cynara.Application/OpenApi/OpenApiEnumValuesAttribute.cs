namespace Cynara.Application.OpenApi;

/// <summary>
/// Documents the allowed wire values for an enum-like string DTO property.
/// The API's <c>CynaraEnumSchemaFilter</c> turns this into an OpenAPI
/// string schema with a fixed value list so client generators can build
/// typed unions. Lives in Application because the annotated DTOs do.
/// </summary>
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
