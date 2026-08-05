using System.Text.Json.Serialization;

namespace Cynara.Domain.Encounters;

/// <summary>
/// Clinical classification of an encounter. Values mirror common FHIR
/// Encounter.class concepts used by the clinical workspace.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EncounterType
{
    /// <summary>Outpatient / ambulatory visit.</summary>
    Ambulatory = 0,

    /// <summary>Emergency department visit.</summary>
    Emergency = 1,

    /// <summary>Inpatient admission.</summary>
    Inpatient = 2,

    /// <summary>Observation / short-stay encounter.</summary>
    Observation = 3,

    /// <summary>Virtual / telehealth encounter.</summary>
    Virtual = 4,
}
