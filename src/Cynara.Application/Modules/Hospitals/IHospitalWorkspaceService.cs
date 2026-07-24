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
/// <para>
/// Field reference:
/// <list type="bullet">
/// <item><c>Id</c> – surrogate Guid; immutable.</item>
/// <item><c>Code</c> – stable business code (pattern
/// <c>^[a-zA-Z0-9][a-zA-Z0-9._-]{0,62}[a-zA-Z0-9]$</c>); immutable.</item>
/// <item><c>Name</c> – human-readable workspace name; mutable.</item>
/// <item><c>Status</c> – one of <c>active</c>, <c>inactive</c>,
/// <c>suspended</c>.</item>
/// <item><c>MetadataJson</c> – optional free-form JSON document; mutable.</item>
/// <item><c>RowVersion</c> – optimistic concurrency token; send the latest
/// value back on PATCH, mismatch returns 409.</item>
/// <item><c>CreatedAt</c> – UTC timestamp; immutable.</item>
/// <item><c>UpdatedAt</c> – UTC timestamp of the last metadata change.</item>
/// </list>
/// </para>
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
/// Update contract for the current workspace. Only mutable display fields
/// and a concurrency token are accepted. Hospital identifier, code, and
/// creation timestamp are immutable and rejected with 400 if supplied.
/// <para>
/// Field reference:
/// <list type="bullet">
/// <item><c>Name</c> – 1-256 characters; replaces the current Name.</item>
/// <item><c>MetadataJson</c> – replacement payload, or null to clear it.</item>
/// <item><c>RowVersion</c> – concurrency token from the last GET; mismatch
/// returns 409.</item>
/// </list>
/// </para>
/// </summary>
public sealed record UpdateHospitalWorkspaceRequest(
    string Name,
    string? MetadataJson,
    uint RowVersion);
