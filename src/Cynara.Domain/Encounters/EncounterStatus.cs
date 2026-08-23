using System.Text.Json.Serialization;

namespace Cynara.Domain.Encounters;

/// <summary>
/// Lifecycle status for a clinical encounter. Open encounters accept
/// transitions; terminal states remain historically queryable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterStatus
{
    /// <summary>Default state for newly created encounters.</summary>
    Open = 0,

    /// <summary>Encounter was completed normally.</summary>
    Completed = 1,

    /// <summary>Encounter was canceled before completion.</summary>
    Canceled = 2,

    /// <summary>
    /// Encounter was recorded in error and must not drive new clinical
    /// activity, but remains readable for audit continuity.
    /// </summary>
    EnteredInError = 3,
}
