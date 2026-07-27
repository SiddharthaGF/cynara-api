using System.Text.Json.Serialization;

namespace Cynara.Domain.Documents;

/// <summary>
/// Lifecycle status of a clinical document catalog entry.
/// <see cref="Active"/> entries can be used to start new document instances;
/// <see cref="Retired"/> entries remain resolvable for historical references
/// but must be rejected by new clinical activity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentDefinitionStatus
{
    /// <summary>Default state. The catalog entry accepts new document instances.</summary>
    Active = 0,

    /// <summary>
    /// Catalog entry is preserved for historical references; new document
    /// instances cannot be created from it.
    /// </summary>
    Retired = 1,
}
