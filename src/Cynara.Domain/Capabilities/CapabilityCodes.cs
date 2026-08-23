namespace Cynara.Domain.Capabilities;

/// <summary>
/// Capability codes. Each code gates a coherent read or mutation surface;
/// read/write pairs per resource family let a tenant grant read-only access.
/// Codes are the stable wire contract for capability assignment and must
/// not be renamed once persisted.
/// </summary>
public static class CapabilityCodes
{
    public const string PatientsRead = "patients.read";

    public const string PatientsWrite = "patients.write";

    public const string EncountersRead = "encounters.read";

    public const string EncountersWrite = "encounters.write";

    public const string ClinicalDocumentsRead = "clinical-documents.read";

    public const string ClinicalDocumentsWrite = "clinical-documents.write";

    public const string FormResponsesRead = "form-responses.read";

    public const string FormResponsesWrite = "form-responses.write";

    public const string AuditRead = "audit.read";

    public const string CatalogRead = "catalog.read";

    public const string CatalogWrite = "catalog.write";

    public const string PipelinesRead = "pipelines.read";

    public const string PipelinesWrite = "pipelines.write";

    public const string TasksRead = "tasks.read";

    public const string TasksWrite = "tasks.write";

    public const string WorkflowsRead = "workflows.read";

    public const string WorkflowsWrite = "workflows.write";

    public const string WorkspaceRead = "workspace.read";

    public const string WorkspaceWrite = "workspace.write";

    public const string CapabilitiesRead = "capabilities.read";

    public const string CapabilitiesWrite = "capabilities.write";

    /// <summary>
    /// Gates the administrative user directory read surface. The same code
    /// serves both grant scopes: breadth comes from the assignment's scope
    /// dimension, never from a scope-encoded code variant.
    /// </summary>
    public const string UsersRead = "users.read";

    /// <summary>
    /// Gates the administrative invitation read surface (listing and
    /// lifecycle inspection); hospital-scoped holders also receive
    /// expiration notifications.
    /// </summary>
    public const string UserInvitationsRead = "user-invitations.read";

    /// <summary>
    /// Gates the administrative invitation mutation surface (create, cancel,
    /// resend). Breadth comes from the assignment's scope dimension, never
    /// from a scope-encoded code variant.
    /// </summary>
    public const string UserInvitationsWrite = "user-invitations.write";

    /// <summary>
    /// The complete Stage 2 catalog. Assignment requests are validated
    /// against this list so an unknown code can never be persisted.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        PatientsRead,
        PatientsWrite,
        EncountersRead,
        EncountersWrite,
        ClinicalDocumentsRead,
        ClinicalDocumentsWrite,
        FormResponsesRead,
        FormResponsesWrite,
        AuditRead,
        CatalogRead,
        CatalogWrite,
        PipelinesRead,
        PipelinesWrite,
        TasksRead,
        TasksWrite,
        WorkflowsRead,
        WorkflowsWrite,
        WorkspaceRead,
        WorkspaceWrite,
        CapabilitiesRead,
        CapabilitiesWrite,
        UsersRead,
        UserInvitationsRead,
        UserInvitationsWrite,
    ];
}
