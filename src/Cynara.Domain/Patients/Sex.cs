using System.Text.Json.Serialization;

namespace Cynara.Domain.Patients;

/// <summary>
/// Biological sex classification stored on the patient aggregate. Values
/// follow the minimal demographic vocabulary required by the registry and
/// are persisted as lowercase strings so the JSON contract stays stable
/// across the application and infrastructure layers.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Sex
{
    /// <summary>Patient sex recorded as female.</summary>
    Female = 0,

    /// <summary>Patient sex recorded as male.</summary>
    Male = 1,

    /// <summary>Patient declined to disclose or value is unknown.</summary>
    Unknown = 2,
}
