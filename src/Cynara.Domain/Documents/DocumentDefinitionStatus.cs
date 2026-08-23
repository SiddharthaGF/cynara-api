using System.Text.Json.Serialization;

namespace Cynara.Domain.Documents;

/// <summary>
/// Lifecycle status of a clinical document catalog entry. Active entries
/// start new document instances; retired ones stay resolvable for history
/// but reject new clinical activity.
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
