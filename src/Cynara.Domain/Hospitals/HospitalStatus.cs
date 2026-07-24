using System.Text.Json.Serialization;

namespace Cynara.Domain.Hospitals;

/// <summary>
/// Lifecycle status of a hospital workspace. Active workspaces accept
/// requests; suspended workspaces retain history but reject writes; archived
/// workspaces are read-only and reserved for historical reporting.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HospitalStatus
{
    /// <summary>Default state. Workspace accepts all tenant operations.</summary>
    Active = 0,

    /// <summary>Workspace retains configuration and clinical data but rejects writes.</summary>
    Suspended = 1,

    /// <summary>Workspace is hidden from new traffic and reserved for reporting.</summary>
    Archived = 2,
}
