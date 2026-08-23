using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Workflows;

/// <summary>
/// Runtime progression of a single subject through a pinned published
/// workflow version. The graph is always read from the immutable version it
/// started on; every transition appends to <see cref="History"/> and is
/// never rewritten. Terminal states remain historically queryable.
/// </summary>
[NoResource]
public sealed class Pipeline
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace. Stamped by application workflows.</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to the pinned published workflow version.</summary>
    public Guid WorkflowVersionId { get; set; }

    /// <summary>
    /// The published workflow version this pipeline runs on. Immutable
    /// after publish; never swapped for a newer version at runtime.
    /// </summary>
    public WorkflowVersion WorkflowVersion { get; set; } = null!;

    /// <summary>Kind of clinical record the pipeline drives.</summary>
    public PipelineSubjectType SubjectType { get; set; }

    /// <summary>Encounter or patient identifier the pipeline drives.</summary>
    public Guid SubjectId { get; set; }

    /// <summary>
    /// Patient record the pipeline journey belongs to; denormalized from the
    /// encounter when <see cref="SubjectType"/> is encounter, otherwise the
    /// subject itself. Always set.
    /// </summary>
    public Guid PatientId { get; set; }

    /// <summary>
    /// Encounter the pipeline is bound to when
    /// <see cref="SubjectType"/> is encounter; <see langword="null"/> for
    /// patient-bound pipelines. Immutable after creation.
    /// </summary>
    public Guid? EncounterId { get; set; }

    /// <summary>Lifecycle status of the pipeline.</summary>
    public PipelineStatus Status { get; set; }

    /// <summary>
    /// Id of the node the pipeline currently sits on. Advances move the
    /// cursor along an outgoing edge of the pinned workflow graph.
    /// </summary>
    public string CurrentNodeId { get; set; } = null!;

    /// <summary>UTC timestamp when the pipeline started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the pipeline ended. Set on completion, cancel, or
    /// enter-in-error; <see langword="null"/> while running.
    /// </summary>
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>UTC timestamp when the pipeline was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last pipeline change.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token; send the latest value back on
    /// transitions. Mismatch returns a concurrency conflict.
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>Append-only progression history, ordered by sequence.</summary>
    public ISet<PipelineHistory> History { get; set; } = new HashSet<PipelineHistory>();
}
