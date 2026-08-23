using System.Text.Json.Serialization;

namespace Cynara.Domain.Documents;

/// <summary>
/// Lifecycle status of a clinical document instance; transitions are
/// state-machine rules enforced by the application layer. Terminal states
/// remain historically queryable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClinicalDocumentStatus
{
    /// <summary>Default state for newly started documents.</summary>
    InProgress = 0,

    /// <summary>Document was completed normally.</summary>
    Completed = 1,

    /// <summary>Document was canceled before completion.</summary>
    Canceled = 2,

    /// <summary>
    /// Document was recorded in error and must not be used clinically, but
    /// remains readable for audit continuity with its reason, actor, and
    /// timestamp.
    /// </summary>
    EnteredInError = 3,
}
