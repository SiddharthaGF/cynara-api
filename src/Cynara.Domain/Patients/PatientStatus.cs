using System.Text.Json.Serialization;

namespace Cynara.Domain.Patients;

/// <summary>
/// Lifecycle status for a patient registry entry. Active records are
/// visible through search and detail; retired records stay in the database
/// for historical audit and form-response continuity.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PatientStatus
{
    /// <summary>Default state for newly created patients.</summary>
    Active = 0,

    /// <summary>Soft-retired state; the record is hidden from default listings.</summary>
    Retired = 1,
}
