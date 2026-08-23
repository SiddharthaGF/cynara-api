using System.Text.Json.Serialization;

namespace Cynara.Domain.ClinicalTaxonomy;

/// <summary>
/// Lifecycle status shared by facilities, clinical areas, and disciplines.
/// Active definitions accept new clinical activity; retired ones stay
/// resolvable for history but reject new references unless explicitly
/// overridden.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClinicalTaxonomyStatus
{
    /// <summary>Default state. Definition accepts new activity.</summary>
    Active = 0,

    /// <summary>
    /// Definition is preserved for historical references; new clinical
    /// activity should reject references unless explicitly allowed by a
    /// migration path.
    /// </summary>
    Retired = 1,
}
