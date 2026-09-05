using Cynara.Api.Common.ActorContext;
using Cynara.Api.Common.ErrorHandling;
using Cynara.Application;
using Cynara.Application.Modules.Memberships;
using Cynara.Domain.Capabilities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cynara.Api.Modules.Memberships;

/// <summary>
/// HTTP surface of membership administration (slice 2: list, add, update;
/// revoke/reactivate arrive in slice 3). Routes carry the capability-code
/// authorization policy so denials short-circuit into the shared 403
/// envelope before any data is touched, and the workflow re-checks
/// tenant context and capability so denial never leaks resource
/// existence. Concurrent duplicate inserts surface 409 through the
/// unique-violation catch — the invitations shape, but Conflict instead
/// of Validation (deliberate divergence, do NOT "fix").
/// </summary>
internal static class MembershipsEndpoints
{
    public static IEndpointRouteBuilder MapMembershipsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder memberships = endpoints
            .MapGroup("/api/memberships")
            .WithTags("Memberships");

        _ = memberships.MapGet("/", ListAsync)
            .RequireAuthorization(CapabilityCodes.MembershipsRead)
            .WithName("ListMemberships")
            .WithSummary("List the hospital's memberships newest-first")
            .Produces<IReadOnlyList<MembershipView>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden);

        _ = memberships.MapPost("/", CreateAsync)
            .RequireAuthorization(CapabilityCodes.MembershipsWrite)
            .WithName("AddMembership")
            .WithSummary("Add one active membership to the hospital")
            .Produces<MembershipView>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        _ = memberships.MapPost("/{id:guid}/update", UpdateAsync)
            .RequireAuthorization(CapabilityCodes.MembershipsWrite)
            .WithName("UpdateMembership")
            .WithSummary("Replace the actor id of an active membership")
            .Produces<MembershipView>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        MembershipAdminWorkflow workflow,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MembershipView> items = await workflow
            .ListAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        AddMembershipRequest request,
        MembershipAdminWorkflow workflow,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        try
        {
            MembershipView created = await workflow.AddAsync(
                request,
                http.GetActorId(),
                cancellationToken).ConfigureAwait(false);
            return Results.Created(
                $"/api/memberships/{created.Id}",
                created);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            return ProblemDetailsMapping.FromException(
                new ConflictException(
                    "The user or actor id already exists in "
                    + "this hospital."));
        }
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateMembershipRequest request,
        MembershipAdminWorkflow workflow,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        try
        {
            MembershipView updated = await workflow.UpdateAsync(
                id,
                request,
                http.GetActorId(),
                cancellationToken).ConfigureAwait(false);
            return Results.Ok(updated);
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(exception))
        {
            return ProblemDetailsMapping.FromException(
                new ConflictException(
                    "The actor id already exists in this hospital."));
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgres
            && string.Equals(
                postgres.SqlState,
                PostgresErrorCodes.UniqueViolation,
                StringComparison.Ordinal);
    }
}
