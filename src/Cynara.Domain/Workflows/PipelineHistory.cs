using JsonApiDotNetCore.Resources.Annotations;

namespace Cynara.Domain.Workflows;

/// <summary>
/// Append-only progression event on a pipeline, created by the pipeline
/// runtime only. APIs can never create, update, or delete history entries.
/// </summary>
[NoResource]
public sealed class PipelineHistory
{
    /// <summary>Surrogate identifier; immutable.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning hospital workspace (denormalized from the pipeline).</summary>
    public Guid HospitalId { get; set; }

    /// <summary>FK to the owning pipeline (infrastructure; use navigation).</summary>
    public Guid PipelineId { get; set; }

    /// <summary>Owning pipeline.</summary>
    public Pipeline Pipeline { get; set; } = null!;

    /// <summary>
    /// Monotonic position within the owning pipeline. Starts at 1 and is
    /// never reused, so history order is stable and gaps cannot be patched.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>Machine-readable event action, e.g. <c>pipeline.advanced</c>.</summary>
    public string Action { get; set; } = null!;

    /// <summary>Acting user or system that caused the event, when known.</summary>
    public string? ActorId { get; set; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>
    /// Canonical JSON metadata describing the transition (from/to node,
    /// edge label, reason, row version, etc.).
    /// </summary>
    public string? MetadataJson { get; set; }
}
