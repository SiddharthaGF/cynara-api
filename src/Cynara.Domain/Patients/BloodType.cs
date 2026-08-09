using System.Text.Json.Serialization;

namespace Cynara.Domain.Patients;

/// <summary>
/// ABO/Rh blood type classification stored on the patient aggregate.
/// Values are persisted as strings so the JSON contract stays stable
/// across the application and infrastructure layers; the canonical
/// wire form is the clinical notation (<c>a+</c>, <c>o-</c>, …).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BloodType
{
    /// <summary>A positive.</summary>
    APositive = 0,

    /// <summary>A negative.</summary>
    ANegative = 1,

    /// <summary>B positive.</summary>
    BPositive = 2,

    /// <summary>B negative.</summary>
    BNegative = 3,

    /// <summary>AB positive.</summary>
    ABPositive = 4,

    /// <summary>AB negative.</summary>
    ABNegative = 5,

    /// <summary>O positive.</summary>
    OPositive = 6,

    /// <summary>O negative.</summary>
    ONegative = 7,
}
