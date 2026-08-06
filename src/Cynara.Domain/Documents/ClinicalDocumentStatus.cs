using System.Text.Json.Serialization;

namespace Cynara.Domain.Documents;

/// <summary>
/// Lifecycle status of a clinical document instance. Documents are created
/// <see cref="InProgress"/> by the start-document workflow; completion is a
/// lifecycle invariant enforced by the application layer. Terminal states
/// remain historically queryable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClinicalDocumentStatus
{
    /// <summary>Default state for newly started documents.</summary>
    InProgress = 0,

    /// <summary>Document was completed normally.</summary>
    Completed = 1,
}
