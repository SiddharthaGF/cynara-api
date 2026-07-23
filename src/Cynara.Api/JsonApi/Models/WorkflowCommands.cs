namespace Cynara.Api.JsonApi.Models;

/// <summary>
/// Command body for concurrency-gated form version transitions.
/// </summary>
public sealed class RowVersionCommand
{
    /// <summary>
    /// Expected optimistic concurrency token from the current resource.
    /// </summary>
    public uint RowVersion { get; set; }
}

/// <summary>
/// Reject-review command requiring a human-readable comment.
/// </summary>
public sealed class RejectReviewCommand
{
    /// <summary>Reviewer comment explaining the rejection.</summary>
    public required string Comment { get; set; }

    /// <summary>Expected optimistic concurrency token.</summary>
    public uint RowVersion { get; set; }
}

/// <summary>
/// Complete-response command carrying the concurrency token.
/// </summary>
public sealed class CompleteResponseCommand
{
    /// <summary>Expected optimistic concurrency token.</summary>
    public uint RowVersion { get; set; }
}
