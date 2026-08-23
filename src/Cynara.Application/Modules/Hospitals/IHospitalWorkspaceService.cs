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
/// a different tenant. <c>Status</c> is active/inactive/suspended;
/// <c>RowVersion</c> must be sent back on PATCH (mismatch returns 409).
/// </summary>
public sealed record HospitalWorkspaceDto(
    Guid Id,
    string Code,
    string Name,
    string Status,
    string? MetadataJson,
    uint RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Update contract for the current workspace: only display fields plus a
/// concurrency token are accepted; identifier, code, and creation timestamp
/// are immutable and rejected with 400 if supplied.
/// </summary>
public sealed record UpdateHospitalWorkspaceRequest(
    string Name,
    string? MetadataJson,
    uint RowVersion);
