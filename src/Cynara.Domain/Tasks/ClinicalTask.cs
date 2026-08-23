using Cynara.Domain.Workflows;

using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Tasks;

/// <summary>
/// A single clinical task generated when a pipeline enters a task node,
/// derived exclusively from the pinned published workflow definition. It
/// snapshots the node's assignee, form reference, and due date, and closes
/// when the document completes or the pipeline terminates.
/// </summary>
[NoResource]
public sealed class ClinicalTask
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to the pipeline that generated this task.</summary>
    public Guid PipelineId { get; set; }

    /// <summary>Generating pipeline (infrastructure; use navigation).</summary>
    public Pipeline Pipeline { get; set; } = null!;

    /// <summary>
    /// The pinned published workflow version the generating pipeline runs
    /// on. Tasks are snapshots of the node at generation time.
    /// </summary>
    public Guid WorkflowVersionId { get; set; }

    /// <summary>
    /// Owning workflow definition, denormalized from the generating pipeline
    /// so task audit events can be queried by workflow without a join.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>Id of the workflow node that generated this task.</summary>
    public string NodeId { get; set; } = null!;

    /// <summary>Display name of the task, from the workflow node.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Optional free-text description, from the workflow node.</summary>
    public string? Description { get; set; }

    /// <summary>Lifecycle status of the task.</summary>
    public ClinicalTaskStatus Status { get; set; }

    /// <summary>Optional actor the task is assigned to, from the definition.</summary>
    public string? AssignedActor { get; set; }

    /// <summary>Optional role the task is assigned to, from the definition.</summary>
    public string? AssignedRole { get; set; }

    /// <summary>Optional discipline the task is assigned to, from the definition.</summary>
    public string? AssignedDiscipline { get; set; }

    /// <summary>Patient record the task belongs to; mirrors the pipeline.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Encounter the task belongs to; mirrors the pipeline.</summary>
    public Guid? EncounterId { get; set; }

    /// <summary>Referenced form code, from the workflow node.</summary>
    public string? FormCode { get; set; }

    /// <summary>Referenced form version, from the workflow node.</summary>
    public string? FormVersion { get; set; }

    /// <summary>
    /// UTC timestamp by which the task should be completed. Derived from the
    /// node's dueDays at generation time; <see langword="null"/> when the
    /// node does not define a due date.
    /// </summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Actor who claimed the task.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>UTC timestamp when the task was claimed.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }

    /// <summary>Actor who completed the task.</summary>
    public string? CompletedBy { get; set; }

    /// <summary>UTC timestamp when the task was completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Actor who canceled the task.</summary>
    public string? CanceledBy { get; set; }

    /// <summary>UTC timestamp when the task was canceled.</summary>
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>UTC timestamp when the task was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last task change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on
    /// transitions. Mismatch returns a concurrency conflict.
    /// </summary>
    public uint RowVersion { get; set; }
}
