namespace Cynara.Application.Modules.Hospitals;

/// <summary>
/// Read and update contract for the current resolved workspace. The service
/// is intentionally driven by the request context rather than a client-supplied
/// identifier to prevent tenant override.
/// </summary>
public interface IHospitalWorkspaceService
{
    /// <summary>Returns the resolved workspace for the current request.</summary>
    public Task<HospitalWorkspaceDto> GetCurrentAsync(
        CancellationToken cancellationToken);

    /// <summary>Updates the resolved workspace for the current request.</summary>
    public Task<HospitalWorkspaceDto> UpdateCurrentAsync(
        UpdateHospitalWorkspaceRequest request,
        string? actorId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Public read shape exposed by the workspace endpoint. Bound to the
/// X-Hospital-Code header from the request context; clients cannot select
/// a different tenant.
/// </summary>
public sealed record HospitalWorkspaceDto(
    /// <summary>Surrogate Guid that identifies the hospital across the platform.</summary>
    /// <remarks>Immutable; clients cannot override it.</remarks>
    Guid Id,

    /// <summary>Stable business code used by clients and URLs.</summary>
    /// <remarks>
    /// Pattern: ^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$. Immutable.
    /// </remarks>
    string Code,

    /// <summary>Human-readable workspace name shown in administrative UIs.</summary>
    /// <remarks>Mutable via PATCH /api/workspace.</remarks>
    string Name,

    /// <summary>Lifecycle status of the workspace.</summary>
    /// <remarks>One of: active, inactive, suspended.</remarks>
    string Status,

    /// <summary>Optional metadata payload stored as a JSON document.</summary>
    /// <remarks>Mutable via PATCH /api/workspace.</remarks>
    string? MetadataJson,

    /// <summary>Optimistic concurrency token; required for PATCH updates.</summary>
    /// <remarks>Send the latest value back on PATCH; mismatch returns 409.</remarks>
    uint RowVersion,

    /// <summary>UTC timestamp when the workspace was created.</summary>
    /// <remarks>Immutable.</remarks>
    DateTimeOffset CreatedAt,

    /// <summary>UTC timestamp of the last workspace metadata change.</summary>
    /// <remarks>Updated by PATCH /api/workspace.</remarks>
    DateTimeOffset UpdatedAt);

/// <summary>
/// Update contract for the current workspace. Only mutable display fields
/// and a concurrency token are accepted. Hospital identifier, code, and
/// creation timestamp are immutable and rejected with 400 if supplied.
/// </summary>
public sealed record UpdateHospitalWorkspaceRequest(
    /// <summary>New human-readable workspace name.</summary>
    /// <remarks>1-256 characters; replaces the current Name.</remarks>
    string Name,

    /// <summary>Replacement metadata payload, or null to clear it.</summary>
    /// <remarks>
    /// A free-form JSON document. Send null to clear the existing payload.
    /// </remarks>
    string? MetadataJson,

    /// <summary>
    /// Concurrency token from the last GET /api/workspace response.
    /// </summary>
    /// <remarks>Mismatched values return 409 Conflict.</remarks>
    uint RowVersion);
