using Cynara.Api.Common.ActorContext;
using Cynara.Application.Audit;
using Cynara.Application.Common;
using Cynara.Application.Modules.Users;
using Cynara.Domain.Capabilities;

namespace Cynara.Api.Modules.Users;

/// <summary>
/// HTTP surface of the administrative user directory. Both routes carry the
/// <c>users.read</c> authorization policy so denials short-circuit into the
/// audited shared 403 envelope before any data is touched; the service layer
/// re-checks tenant context and capability so denial never leaks resource
/// existence. Successful detail reads emit one sensitive-read audit event
/// after they succeed; listings are never audited.
/// </summary>
internal static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder users = endpoints
            .MapGroup("/api/users")
            .WithTags("Users");

        _ = users.MapGet("/", ListUsersAsync)
            .RequireAuthorization(CapabilityCodes.UsersRead)
            .WithName("ListUsers")
            .WithSummary(
                "List directory users inside the caller's users.read scope "
                + "(platform grants span all hospitals)")
            .Produces<UserDirectoryListResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        _ = users.MapGet("/{id:guid}", GetUserAsync)
            .RequireAuthorization(CapabilityCodes.UsersRead)
            .WithName("GetUser")
            .WithSummary("Get one in-scope directory user")
            .Produces<UserDirectoryDetail>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListUsersAsync(
        string? q,
        Guid? hospital,
        int? page,
        int? pageSize,
        IUserDirectoryService directory,
        CancellationToken cancellationToken)
    {
        UserDirectoryListResponse response = await directory.SearchAsync(
            new UserDirectorySearchRequest(
                q,
                hospital,
                page ?? 1,
                pageSize ?? UserDirectoryFieldLimits.DefaultPageSize),
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(response);
    }

    private static async Task<IResult> GetUserAsync(
        Guid id,
        ISensitiveReadAuditor sensitiveReadAuditor,
        IUserDirectoryService directory,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        UserDirectoryDetail detail = await directory.FindDetailAsync(
            id,
            hospitalFilter: null,
            cancellationToken).ConfigureAwait(false);
        await sensitiveReadAuditor.RecordAsync(
            AuditEntityTypes.User,
            detail.Id,
            "user.read",
            http.GetActorId(),
            http.Request.Path,
            cancellationToken).ConfigureAwait(false);
        return Results.Ok(detail);
    }
}
