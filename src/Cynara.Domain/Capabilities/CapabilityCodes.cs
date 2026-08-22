namespace Cynara.Domain.Capabilities;

/// <summary>
/// Stage 2 and Stage 3 capability codes. Each code gates a coherent read or
/// mutation surface; a read and write pair is defined per resource family so
/// a tenant can grant read-only access without conferring mutation rights.
/// Stage 3 adds the workflow family: <see cref="WorkflowsRead"/> /
/// <see cref="WorkflowsWrite"/> gate workflow configuration (define, review,
/// publish, retire), while <see cref="PipelinesRead"/> /
/// <see cref="PipelinesWrite"/> and <see cref="TasksRead"/> /
/// <see cref="TasksWrite"/> gate the pipeline and task runtimes. The codes
/// are the stable wire contract for capability assignment (see
/// <c>Cynara.Api.JsonApi.Controllers.CapabilityAssignmentsController</c>)
/// and must not be renamed once persisted.
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
    ];
}
