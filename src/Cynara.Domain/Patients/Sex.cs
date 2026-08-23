using System.Text.Json.Serialization;

namespace Cynara.Domain.Patients;

/// <summary>
/// Biological sex classification on the patient aggregate, persisted as
/// lowercase strings so the JSON contract stays stable.
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
