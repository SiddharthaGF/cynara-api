using System.Text.Json.Serialization;

namespace Cynara.Domain.Memberships;

/// <summary>
/// Lifecycle status of a hospital membership period row. Active is the
/// only resolving state; revoked rows are retained history and sit
/// outside the active-only uniqueness window.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MembershipStatus
{
    /// <summary>Default state. Membership resolves to its actor id.</summary>
    Active = 0,

    /// <summary>Terminal state. Row retained as history, never
    /// resolves.</summary>
    Revoked = 1,
}
