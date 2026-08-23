using System.Text.Json.Serialization;

namespace Cynara.Domain.Patients;

/// <summary>
/// ABO/Rh blood type classification on the patient aggregate, persisted as
/// strings so the JSON contract stays stable; canonical wire form is
/// clinical notation (a+, o-, …).
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
